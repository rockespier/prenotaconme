using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Notifications;
using PrenotaConMe.Shared.Tenancy;
using PrenotaConMe.Shared.Validation;

namespace PrenotaConMe.Modules.Scheduling;

public sealed record SlotDto(string Time, DateTimeOffset StartsAt, IReadOnlyList<Guid> StaffIds);

public sealed record AvailabilityDayDto(DateOnly Date, IReadOnlyList<SlotDto> Slots);

public sealed record AvailabilityDto(Guid LocationId, Guid ServiceId, string TimeZone, int DurationMinutes, IReadOnlyList<AvailabilityDayDto> Days);

public sealed record BookAppointmentRequest(Guid LocationId, Guid ServiceId, Guid? StaffId, DateTimeOffset StartsAt);

public sealed record AppointmentDto(
    Guid Id, Guid LocationId, Guid ServiceId, string ServiceName, decimal Price, Guid StaffMemberId,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, AppointmentStatus Status);

internal static class SchedulingEndpoints
{
    public const int MaxDays = 31;

    public static void Map(IEndpointRouteBuilder group)
    {
        // F1-10: public booking page data (business chosen with X-Tenant).
        group.MapGet("/booking-options", BookingOptions.GetAsync)
            .AllowAnonymous()
            .RequireTenant()
            .WithName("GetBookingOptions");

        // F1-08: public, used by the booking page (business chosen with X-Tenant).
        group.MapGet("/availability", GetAvailabilityAsync)
            .AllowAnonymous()
            .RequireTenant()
            .WithName("GetAvailability");

        // F1-09: a signed-in client books a slot.
        group.MapPost("/appointments", BookAsync)
            .RequireAuthorization()
            .RequireTenant()
            .WithName("BookAppointment");

        // F1-12: the client's own appointments (list, cancel, reschedule).
        MyAppointmentsEndpoints.Map(group);

        // F1-13: backoffice calendar.
        CalendarEndpoints.Map(group);

        // F1-14: client list and history.
        ClientsEndpoints.Map(group);
    }

    /// <param name="rescheduling">
    /// Id of the caller's own appointment being moved: its current time then counts as free. Ignored for anyone else.
    /// </param>
    private static async Task<Results<Ok<AvailabilityDto>, ValidationProblem>> GetAvailabilityAsync(
        Guid locationId, Guid serviceId, DateOnly from, int? days, Guid? staffId, Guid? rescheduling,
        AvailabilityService availability, AppDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var count = days ?? 7;
        if (count is < 1 or > MaxDays)
        {
            return new ValidationErrors().Add("days", "days.invalid").ToProblem();
        }

        Guid? ignore = null;
        if (rescheduling is not null && currentUser.IsAuthenticated)
        {
            var userId = await currentUser.GetUserIdAsync(ct);
            if (await db.Set<Appointment>().AnyAsync(a => a.Id == rescheduling && a.ClientUserId == userId, ct))
            {
                ignore = rescheduling;
            }
        }

        var result = await availability.GetAsync(
            new AvailabilityQuery(locationId, serviceId, staffId, from, from.AddDays(count - 1), ignore), ct);
        if (result.Error is not null)
        {
            return new ValidationErrors().Add("availability", result.Error).ToProblem();
        }

        var dto = new AvailabilityDto(
            locationId, serviceId, result.Location!.TimeZone, result.Service!.DurationMinutes,
            result.Slots
                .GroupBy(s => s.Date)
                .Select(g => new AvailabilityDayDto(g.Key, g.Select(s => new SlotDto(s.LocalTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), s.StartsAt, s.StaffIds)).ToList()))
                .ToList());

        return TypedResults.Ok(dto);
    }

    private static async Task<Results<Created<AppointmentDto>, ValidationProblem, ProblemHttpResult>> BookAsync(
        BookAppointmentRequest request, AvailabilityService availability, AppDbContext db,
        ICurrentUser currentUser, IMembershipWriter memberships, ITenantContext tenant,
        AppointmentEvents events, CancellationToken ct)
    {
        // Recompute the availability of that local day: the requested start must be one of the offered slots.
        var location = await db.Set<PrenotaConMe.Modules.Locations.Domain.Location>().AsNoTracking()
            .SingleOrDefaultAsync(l => l.Id == request.LocationId, ct);
        if (location is null)
        {
            return new ValidationErrors().Add("locationId", "location.notFound").ToProblem();
        }

        var date = AvailabilityService.LocalDate(request.StartsAt, location.TimeZone);
        var result = await availability.GetAsync(new AvailabilityQuery(request.LocationId, request.ServiceId, request.StaffId, date, date), ct);
        if (result.Error is not null)
        {
            return new ValidationErrors().Add("availability", result.Error).ToProblem();
        }

        var slot = result.Slots.FirstOrDefault(s => s.StartsAt == request.StartsAt.ToUniversalTime());
        if (slot is null)
        {
            return SlotTaken("slot.unavailable");
        }

        var staffId = request.StaffId ?? await LeastBusyAsync(db, slot.StaffIds, request.StartsAt, location.TimeZone, ct);
        var clientId = await currentUser.GetUserIdAsync(ct);
        var service = result.Service!;

        var appointment = Appointment.Book(
            request.LocationId, service.Id, service.Name, service.Price, staffId, clientId, slot.StartsAt, service.DurationMinutes);
        db.Add(appointment);
        await memberships.EnsureAsync(tenant.TenantId!.Value, clientId, TenantRole.Client, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (DbErrors.IsExclusionViolation(ex))
        {
            // Someone booked the same staff member at an overlapping time in the meantime.
            return SlotTaken("slot.taken");
        }

        // F1-11 / F2-02: confirmation e-mails and push notifications are sent in the background.
        await events.PublishAsync(appointment, AppointmentChange.Booked, clientId, ct);

        return TypedResults.Created($"/api/scheduling/appointments/{appointment.Id}", ToDto(appointment));
    }

    /// <summary>"Any staff": the free person with the fewest appointments that local day (ties: stable by id).</summary>
    internal static async Task<Guid> LeastBusyAsync(
        AppDbContext db, IReadOnlyList<Guid> candidates, DateTimeOffset startsAt, string timeZoneId, CancellationToken ct,
        Guid? ignoreAppointmentId = null)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var day = AvailabilityService.LocalDate(startsAt, timeZoneId);
        var dayStart = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), tz), TimeSpan.Zero);
        var dayEnd = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), tz), TimeSpan.Zero);

        var counts = await db.Set<Appointment>()
            .Where(a => candidates.Contains(a.StaffMemberId) && a.Status != AppointmentStatus.Cancelled
                        && a.Id != ignoreAppointmentId
                        && a.StartsAt >= dayStart && a.StartsAt < dayEnd)
            .GroupBy(a => a.StaffMemberId)
            .Select(g => new { StaffId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StaffId, x => x.Count, ct);

        return candidates.OrderBy(id => counts.GetValueOrDefault(id)).ThenBy(id => id).First();
    }

    internal static ProblemHttpResult SlotTaken(string code) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: code,
            detail: "The selected time is no longer available.");

    internal static AppointmentDto ToDto(Appointment a) => new(
        a.Id, a.LocationId, a.ServiceId, a.ServiceName, a.Price, a.StaffMemberId, a.StartsAt, a.EndsAt, a.Status);
}
