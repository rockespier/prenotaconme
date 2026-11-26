using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Chatbot.Domain;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff.Domain;

namespace PrenotaConMe.Modules.Chatbot;

/// <summary>
/// The functions the model can call, run for the client linked to the chat in the current tenant. Booking,
/// moving and cancelling go through <see cref="ClientBookingService"/> (same rules as the website) and only run
/// when the client confirmed the exact same action in a later message: the first call just returns a summary.
/// </summary>
public sealed class BookingTools(AppDbContext db, AvailabilityService availability, ClientBookingService booking, TimeProvider clock)
{
    public const int MaxAvailabilityDays = 7;

    private const string LocalFormat = "yyyy-MM-dd'T'HH:mm";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static readonly IReadOnlyList<ChatToolDefinition> Definitions =
    [
        new("list_services",
            "Services of the business with duration and price, its locations and the staff who do each service.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("get_availability",
            "Free start times (business local time) for a service, from a date for some days. Always use this before proposing times.",
            $$$"""
            {"type":"object","properties":{
              "service_id":{"type":"string"},
              "date":{"type":"string","description":"First day, yyyy-MM-dd"},
              "days":{"type":"integer","minimum":1,"maximum":{{{MaxAvailabilityDays}}},"description":"Number of days, default 1"},
              "staff_id":{"type":"string","description":"Only this staff member (omit for anyone)"},
              "location_id":{"type":"string","description":"Needed only when the service is offered at more than one location"}},
             "required":["service_id","date"],"additionalProperties":false}
            """),
        new("book",
            "Books a free time for the client. The first call returns a summary to confirm; call again with the same arguments after the client says yes.",
            """
            {"type":"object","properties":{
              "service_id":{"type":"string"},
              "start":{"type":"string","description":"Local start, yyyy-MM-ddTHH:mm, one of the free times"},
              "staff_id":{"type":"string","description":"Omit when the client has no preference"},
              "location_id":{"type":"string"}},
             "required":["service_id","start"],"additionalProperties":false}
            """),
        new("my_appointments",
            "The client's upcoming appointments with this business.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("cancel",
            "Cancels one of the client's appointments. The first call returns a summary to confirm; call again with the same arguments after the client says yes.",
            """{"type":"object","properties":{"appointment_id":{"type":"string"}},"required":["appointment_id"],"additionalProperties":false}"""),
        new("reschedule",
            "Moves one of the client's appointments to another free time (same service). The first call returns a summary to confirm; call again with the same arguments after the client says yes.",
            """
            {"type":"object","properties":{
              "appointment_id":{"type":"string"},
              "start":{"type":"string","description":"New local start, yyyy-MM-ddTHH:mm"},
              "staff_id":{"type":"string","description":"Omit to keep anyone free"}},
             "required":["appointment_id","start"],"additionalProperties":false}
            """),
        new("handoff",
            "Hands the chat to a person at the business and pauses you in this chat. Use it when the client asks for a person, complains, or needs something the other tools cannot do. Not for a time that is not free: offer other times instead.",
            """
            {"type":"object","properties":{
              "reason":{"type":"string","description":"What the client needs, one short sentence for the business owner"}},
             "required":["reason"],"additionalProperties":false}
            """),
    ];

    /// <summary>Runs one call and returns its JSON result for the model. Bad arguments come back as errors, never exceptions.</summary>
    public async Task<string> ExecuteAsync(ToolCall call, ChatConversation conversation, string language, CancellationToken ct)
    {
        Args args;
        try
        {
            args = new Args(JsonDocument.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments).RootElement.Clone());
        }
        catch (JsonException)
        {
            return Error("invalid_arguments", "Arguments must be a JSON object.");
        }

        var culture = Culture(language);
        try
        {
            return call.Name switch
            {
                "list_services" => await ListServicesAsync(ct),
                "get_availability" => await GetAvailabilityAsync(args, culture, ct),
                "my_appointments" => await MyAppointmentsAsync(conversation.UserId, culture, ct),
                "book" => await BookAsync(args, conversation, culture, ct),
                "cancel" => await CancelAsync(args, conversation, culture, ct),
                "reschedule" => await RescheduleAsync(args, conversation, culture, ct),
                "handoff" => Handoff(args, conversation),
                _ => Error("unknown_tool", $"There is no tool named {call.Name}."),
            };
        }
        catch (ArgumentException ex)
        {
            return Error("invalid_arguments", ex.Message);
        }
    }

    /// <summary>F3-06: the engine sees <see cref="ChatConversation.IsHandedOff"/>, alerts the Admins and replies itself.</summary>
    private string Handoff(Args args, ChatConversation conversation)
    {
        conversation.Handoff(args.String("reason"), clock.GetUtcNow());
        return Serialize(new { Status = "handed_off" });
    }

    private async Task<string> ListServicesAsync(CancellationToken ct)
    {
        var options = await BookingOptions.LoadAsync(db, ct);
        return Serialize(new
        {
            locations = options.Locations.Select(l => new { l.Id, l.Name, l.Address, l.City, l.Phone }),
            services = options.Services.Select(s => new
            {
                s.Id, s.Name, s.Description, s.DurationMinutes, PriceEur = s.Price, s.LocationIds,
            }),
            staff = options.Staff.Select(s => new { s.Id, Name = s.DisplayName, s.ServiceIds, s.LocationIds }),
        });
    }

    private async Task<string> GetAvailabilityAsync(Args args, CultureInfo culture, CancellationToken ct)
    {
        var serviceId = args.Guid("service_id");
        var from = args.Date("date");
        var days = Math.Clamp(args.Int("days") ?? 1, 1, MaxAvailabilityDays);
        var staffId = args.OptionalGuid("staff_id");
        if (await ResolveLocationAsync(serviceId, args.OptionalGuid("location_id"), ct) is not { } locationId)
        {
            return await LocationRequiredAsync(ct);
        }

        var result = await availability.GetAsync(new AvailabilityQuery(locationId, serviceId, staffId, from, from.AddDays(days - 1)), ct);
        if (result.Error is not null)
        {
            return ErrorFor(result.Error);
        }

        var byDay = result.Slots.GroupBy(s => s.Date).Select(g => new
        {
            Date = g.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Day = g.Key.ToString("dddd d MMMM", culture),
            Times = g.Select(s => s.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture)).ToList(),
        }).ToList();

        return Serialize(new
        {
            Service = result.Service!.Name,
            result.Service.DurationMinutes,
            Location = result.Location!.Name,
            Days = byDay,
            Note = byDay.Count == 0 ? "No free times in these days: try the following days." : null,
        });
    }

    private async Task<string> MyAppointmentsAsync(Guid userId, CultureInfo culture, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var notice = await booking.NoticeHoursAsync(ct);
        var appointments = await db.Set<Appointment>().AsNoTracking()
            .Where(a => a.ClientUserId == userId && a.Status == AppointmentStatus.Confirmed && a.StartsAt >= now)
            .OrderBy(a => a.StartsAt)
            .Take(10)
            .ToListAsync(ct);
        var (locations, staff) = await NamesAsync(appointments, ct);

        return Serialize(new
        {
            CancellationNoticeHours = notice,
            Appointments = appointments.Select(a => new
            {
                a.Id,
                Service = a.ServiceName,
                When = Describe(a.StartsAt, locations[a.LocationId].TimeZone, culture),
                Staff = staff[a.StaffMemberId],
                Location = locations[a.LocationId].Name,
                PriceEur = a.Price,
                CanChange = a.CanChange(now, notice),
            }),
        });
    }

    private async Task<string> BookAsync(Args args, ChatConversation conversation, CultureInfo culture, CancellationToken ct)
    {
        var serviceId = args.Guid("service_id");
        var staffId = args.OptionalGuid("staff_id");
        if (await ResolveLocationAsync(serviceId, args.OptionalGuid("location_id"), ct) is not { } locationId)
        {
            return await LocationRequiredAsync(ct);
        }

        var location = await db.Set<Location>().AsNoTracking().SingleAsync(l => l.Id == locationId, ct);
        if (ToInstant(args.String("start"), location.TimeZone) is not { } start)
        {
            return InvalidStart();
        }

        var action = Action("book", serviceId, locationId, staffId, start.Local);
        if (!conversation.IsConfirmed(action))
        {
            var day = start.Local.Date;
            var check = await availability.GetAsync(new AvailabilityQuery(locationId, serviceId, staffId, DateOnly.FromDateTime(day), DateOnly.FromDateTime(day)), ct);
            if (check.Error is not null)
            {
                return ErrorFor(check.Error);
            }

            if (!check.Slots.Any(s => s.StartsAt == start.Utc))
            {
                return ErrorFor("slot.unavailable");
            }

            var staffName = staffId is { } id ? await StaffNameAsync(id, ct) : null;
            conversation.Propose(action);
            return Confirmation(string.Join(", ", new[]
            {
                check.Service!.Name, Describe(start.Utc, location.TimeZone, culture), staffName, location.Name,
                check.Service.Price.ToString("0.00", culture) + " €",
            }.Where(x => !string.IsNullOrWhiteSpace(x))));
        }

        conversation.ClearPending();
        var result = await booking.BookAsync(conversation.UserId, new BookAppointmentRequest(locationId, serviceId, staffId, start.Utc), ct);
        return result.Error is { } error ? ErrorFor(error.Code) : await DoneAsync("booked", result.Appointment!, culture, ct);
    }

    private async Task<string> CancelAsync(Args args, ChatConversation conversation, CultureInfo culture, CancellationToken ct)
    {
        var appointmentId = args.Guid("appointment_id");
        var action = Action("cancel", appointmentId, null, null, null);
        if (!conversation.IsConfirmed(action))
        {
            if (await ChangeableAsync(conversation.UserId, appointmentId, ct) is not { } appointment)
            {
                return await RefusalAsync(conversation.UserId, appointmentId, ct);
            }

            conversation.Propose(action);
            return Confirmation(await DescribeAsync(appointment, culture, ct));
        }

        conversation.ClearPending();
        var result = await booking.CancelAsync(conversation.UserId, appointmentId, ct);
        return result.Error is { } error ? ErrorFor(error.Code) : await DoneAsync("cancelled", result.Appointment!, culture, ct);
    }

    private async Task<string> RescheduleAsync(Args args, ChatConversation conversation, CultureInfo culture, CancellationToken ct)
    {
        var appointmentId = args.Guid("appointment_id");
        var staffId = args.OptionalGuid("staff_id");
        if (await ChangeableAsync(conversation.UserId, appointmentId, ct) is not { } appointment)
        {
            return await RefusalAsync(conversation.UserId, appointmentId, ct);
        }

        var location = await db.Set<Location>().AsNoTracking().SingleAsync(l => l.Id == appointment.LocationId, ct);
        if (ToInstant(args.String("start"), location.TimeZone) is not { } start)
        {
            return InvalidStart();
        }

        var action = Action("reschedule", appointmentId, null, staffId, start.Local);
        if (!conversation.IsConfirmed(action))
        {
            var day = DateOnly.FromDateTime(start.Local.Date);
            var check = await availability.GetAsync(
                new AvailabilityQuery(appointment.LocationId, appointment.ServiceId, staffId, day, day, appointment.Id), ct);
            if (check.Error is not null)
            {
                return ErrorFor(check.Error);
            }

            if (!check.Slots.Any(s => s.StartsAt == start.Utc))
            {
                return ErrorFor("slot.unavailable");
            }

            var staffName = staffId is { } id ? await StaffNameAsync(id, ct) : null;
            conversation.Propose(action);
            return Confirmation(string.Join(" → ", await DescribeAsync(appointment, culture, ct),
                string.Join(", ", new[] { Describe(start.Utc, location.TimeZone, culture), staffName }.Where(x => x is not null))));
        }

        conversation.ClearPending();
        var result = await booking.RescheduleAsync(conversation.UserId, appointmentId, new RescheduleAppointmentRequest(start.Utc, staffId), ct);
        return result.Error is { } error ? ErrorFor(error.Code) : await DoneAsync("rescheduled", result.Appointment!, culture, ct);
    }

    /// <summary>The client's own appointment, if it can still be changed (confirmed and before the notice).</summary>
    private async Task<Appointment?> ChangeableAsync(Guid userId, Guid appointmentId, CancellationToken ct)
    {
        var appointment = await db.Set<Appointment>().AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == appointmentId && a.ClientUserId == userId, ct);
        var notice = await booking.NoticeHoursAsync(ct);
        return appointment is not null && appointment.CanChange(clock.GetUtcNow(), notice) ? appointment : null;
    }

    private async Task<string> RefusalAsync(Guid userId, Guid appointmentId, CancellationToken ct)
    {
        var appointment = await db.Set<Appointment>().AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == appointmentId && a.ClientUserId == userId, ct);
        return appointment switch
        {
            null => ErrorFor("appointment.notFound"),
            { Status: not AppointmentStatus.Confirmed } => ErrorFor("appointment.notConfirmed"),
            _ => ErrorFor("appointment.tooLate", await booking.NoticeHoursAsync(ct)),
        };
    }

    /// <summary>The given location, or the only one offering the service; null when the client must choose.</summary>
    private async Task<Guid?> ResolveLocationAsync(Guid serviceId, Guid? locationId, CancellationToken ct)
    {
        if (locationId is not null)
        {
            return locationId;
        }

        var options = await BookingOptions.LoadAsync(db, ct);
        var locations = options.Services.FirstOrDefault(s => s.Id == serviceId)?.LocationIds ?? [];
        return locations.Count == 1 ? locations[0] : options.Locations.Count == 1 ? options.Locations[0].Id : null;
    }

    private async Task<string> LocationRequiredAsync(CancellationToken ct)
    {
        var locations = await db.Set<Location>().AsNoTracking().Where(l => l.IsActive).Select(l => new { l.Id, l.Name, l.City }).ToListAsync(ct);
        return Serialize(new { Error = "location_required", Hint = "Ask the client which location and pass location_id.", Locations = locations });
    }

    private async Task<string> DoneAsync(string status, Appointment appointment, CultureInfo culture, CancellationToken ct) =>
        Serialize(new { Status = status, Summary = await DescribeAsync(appointment, culture, ct) });

    private async Task<string> DescribeAsync(Appointment a, CultureInfo culture, CancellationToken ct)
    {
        var (locations, staff) = await NamesAsync([a], ct);
        var location = locations[a.LocationId];
        return string.Join(", ", a.ServiceName, Describe(a.StartsAt, location.TimeZone, culture), staff[a.StaffMemberId], location.Name);
    }

    private async Task<(Dictionary<Guid, Location> Locations, Dictionary<Guid, string> Staff)> NamesAsync(
        IReadOnlyCollection<Appointment> appointments, CancellationToken ct)
    {
        var locationIds = appointments.Select(a => a.LocationId).Distinct().ToList();
        var staffIds = appointments.Select(a => a.StaffMemberId).Distinct().ToList();
        var locations = await db.Set<Location>().AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var staff = await db.Set<StaffMember>().AsNoTracking().Where(s => staffIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.DisplayName, ct);
        return (locations, staff);
    }

    private Task<string?> StaffNameAsync(Guid id, CancellationToken ct) =>
        db.Set<StaffMember>().AsNoTracking().Where(s => s.Id == id).Select(s => s.DisplayName).SingleOrDefaultAsync(ct);

    /// <summary>"mercoledì 2 dicembre alle 10:30" style, in the business language.</summary>
    internal static string Describe(DateTimeOffset utc, string timeZone, CultureInfo culture)
    {
        var local = TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(timeZone));
        return local.ToString("dddd d MMMM, HH:mm", culture);
    }

    /// <summary>A local "yyyy-MM-ddTHH:mm" (or an ISO time with offset) at the location; null for invalid or skipped (DST) times.</summary>
    internal static (DateTime Local, DateTimeOffset Utc)? ToInstant(string value, string timeZone)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        string[] formats = [LocalFormat, "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm:ss"];
        if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            if (tz.IsInvalidTime(local))
            {
                return null;
            }

            var utc = new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
            return (local, utc);
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
        {
            var converted = TimeZoneInfo.ConvertTime(withOffset, tz);
            return (converted.DateTime, withOffset.ToUniversalTime());
        }

        return null;
    }

    private static string Action(string tool, Guid id, Guid? locationId, Guid? staffId, DateTime? start) =>
        JsonSerializer.Serialize(new
        {
            tool,
            id,
            location = locationId,
            staff = staffId,
            start = start?.ToString(LocalFormat, CultureInfo.InvariantCulture),
        });

    private static string Confirmation(string summary) => Serialize(new
    {
        Status = "confirmation_required",
        Summary = summary,
        Instruction = "Show the client these exact details and ask them to confirm. Only after they say yes, call the same tool again with the same arguments.",
    });

    private static string InvalidStart() =>
        Error("invalid_start", "Use the business local time as yyyy-MM-ddTHH:mm, one of the times from get_availability.");

    private static string ErrorFor(string code, int? noticeHours = null) => Error(code, code switch
    {
        "slot.unavailable" or "slot.taken" => "That time is not free. Use get_availability and offer other times.",
        "appointment.tooLate" => $"Too late to change it here: the business asks for {noticeHours?.ToString(CultureInfo.InvariantCulture) ?? "some"} hours notice. Suggest calling the business.",
        "appointment.notConfirmed" => "That appointment is already cancelled or past.",
        "appointment.notFound" => "The client has no such appointment. Use my_appointments.",
        "service.notAvailable" => "This service cannot be booked there. Use list_services.",
        "staff.notAvailable" => "This staff member does not do this service there. Use list_services.",
        "location.notFound" => "Unknown location. Use list_services.",
        _ => "The request could not be completed.",
    });

    private static string Error(string code, string hint) => Serialize(new { Error = code, Hint = hint });

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);

    private static CultureInfo Culture(string language) => CultureInfo.GetCultureInfo(language switch
    {
        "es" => "es-ES",
        "en" => "en-GB",
        _ => "it-IT",
    });

    /// <summary>Typed access to the call arguments; a missing or malformed required one throws <see cref="ArgumentException"/>.</summary>
    private sealed class Args(JsonElement root)
    {
        public string String(string name) =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()!
                : throw new ArgumentException($"{name} is required.");

        public Guid Guid(string name) =>
            System.Guid.TryParse(String(name), out var id) ? id : throw new ArgumentException($"{name} must be an id from the tools.");

        public Guid? OptionalGuid(string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
                ? Guid(name)
                : null;

        public DateOnly Date(string name) =>
            DateOnly.TryParseExact(String(name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d
                : throw new ArgumentException($"{name} must be yyyy-MM-dd.");

        public int? Int(string name) => root.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : null;
    }
}
