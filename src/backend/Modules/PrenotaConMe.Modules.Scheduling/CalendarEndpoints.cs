using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Catalog.Domain;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Notifications;
using PrenotaConMe.Shared.Tenancy;
using PrenotaConMe.Shared.Validation;

namespace PrenotaConMe.Modules.Scheduling;

/// <param name="IsGuest">Entered by the business for a client without an account.</param>
/// <param name="ClientKey">Key of the client card (F1-14).</param>
/// <param name="ChargedAmount">F4-01: amount charged when it differs from the price (completed appointments).</param>
public sealed record CalendarAppointmentDto(
    Guid Id, Guid LocationId, Guid ServiceId, string ServiceName, decimal Price, Guid StaffMemberId,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, AppointmentStatus Status,
    string? ClientName, string? ClientEmail, string? ClientPhone, bool IsGuest, string ClientKey, decimal? ChargedAmount);

/// <param name="Outcome">Completed, NoShow, or Confirmed to undo.</param>
/// <param name="ChargedAmount">Only for Completed; omit when the client paid the list price.</param>
public sealed record AppointmentOutcomeRequest(AppointmentStatus Outcome, decimal? ChargedAmount);

public sealed record CalendarStaffDto(Guid Id, string DisplayName, bool IsActive, IReadOnlyList<Guid> LocationIds, IReadOnlyList<Guid> ServiceIds);

/// <param name="CanEdit">Admin: may create, move and cancel. Staff only see their own appointments.</param>
public sealed record CalendarDto(bool CanEdit, IReadOnlyList<CalendarStaffDto> Staff, IReadOnlyList<CalendarAppointmentDto> Appointments);

public sealed record CreateCalendarAppointmentRequest(
    Guid LocationId, Guid ServiceId, Guid StaffId, DateTimeOffset StartsAt, string ClientName, string? ClientPhone);

public sealed record MoveCalendarAppointmentRequest(Guid StaffId, DateTimeOffset StartsAt);

/// <summary>F1-13: backoffice calendar. Admins manage every appointment; Staff see their own.</summary>
internal static class CalendarEndpoints
{
    public const int MaxRangeDays = 62;

    public const decimal MaxChargedAmount = 10_000m;

    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/calendar", GetAsync)
            .RequireTenantRole(TenantRole.Admin, TenantRole.Staff)
            .WithName("GetCalendar");

        group.MapPost("/calendar/appointments", CreateAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("CreateCalendarAppointment");

        group.MapPut("/calendar/appointments/{id:guid}", MoveAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("MoveCalendarAppointment");

        group.MapPost("/calendar/appointments/{id:guid}/cancel", CancelAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("CancelCalendarAppointment");

        group.MapPost("/calendar/appointments/{id:guid}/outcome", OutcomeAsync)
            .RequireTenantRole(TenantRole.Admin, TenantRole.Staff)
            .WithName("RecordAppointmentOutcome");

        // F2-10: open calendars refetch when an appointment changes (WebSocket, so token and tenant may come in the query).
        group.MapHub<CalendarHub>(CalendarHub.Path)
            .RequireTenantRole(TenantRole.Admin, TenantRole.Staff)
            .AcceptCredentialsInQuery();
    }

    private static async Task<Results<Ok<CalendarDto>, ValidationProblem>> GetAsync(
        DateTimeOffset from, DateTimeOffset to, Guid? locationId, Guid? staffId, bool? includeCancelled,
        HttpContext http, IAuthorizationService authorization, ICurrentUser currentUser, IUserDirectory users,
        AppDbContext db, CancellationToken ct)
    {
        if (to <= from || to - from > TimeSpan.FromDays(MaxRangeDays))
        {
            return new ValidationErrors().Add("to", "range.invalid").ToProblem();
        }

        var isAdmin = (await authorization.AuthorizeAsync(http.User, null, new TenantRoleRequirement([TenantRole.Admin]))).Succeeded;

        var staffQuery = db.Set<StaffMember>().AsNoTracking();
        if (!isAdmin)
        {
            // Staff: only the staff record(s) linked to their account.
            var userId = await currentUser.GetUserIdAsync(ct);
            staffQuery = staffQuery.Where(s => s.UserId == userId);
        }

        var staff = await staffQuery.Include(s => s.Locations).Include(s => s.Services).OrderBy(s => s.DisplayName).ToListAsync(ct);
        var visibleStaffIds = staff.Select(s => s.Id).ToList();

        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();
        var appointments = await db.Set<Appointment>().AsNoTracking()
            .Where(a => visibleStaffIds.Contains(a.StaffMemberId)
                        && (staffId == null || a.StaffMemberId == staffId)
                        && (locationId == null || a.LocationId == locationId)
                        && (includeCancelled == true || a.Status != AppointmentStatus.Cancelled)
                        && a.StartsAt < toUtc && a.EndsAt > fromUtc)
            .OrderBy(a => a.StartsAt)
            .ToListAsync(ct);

        var clientIds = appointments.Where(a => a.ClientUserId is not null).Select(a => a.ClientUserId!.Value).Distinct().ToList();
        var clients = await users.GetByIdsAsync(clientIds, ct);

        return TypedResults.Ok(new CalendarDto(
            isAdmin,
            staff.Select(s => new CalendarStaffDto(
                s.Id, s.DisplayName, s.IsActive, s.Locations.Select(l => l.LocationId).ToList(), s.Services.Select(x => x.ServiceId).ToList())).ToList(),
            appointments.Select(a => ToDto(a, a.ClientUserId is { } id ? clients.GetValueOrDefault(id) : null)).ToList()));
    }

    private static async Task<Results<Created<CalendarAppointmentDto>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        CreateCalendarAppointmentRequest request, AppDbContext db, ICurrentUser currentUser, AppointmentEvents events,
        CancellationToken ct)
    {
        var name = request.ClientName?.Trim() ?? string.Empty;
        var phone = request.ClientPhone?.Trim();
        var errors = new ValidationErrors()
            .AddIf(name.Length == 0, "clientName", "clientName.required")
            .AddIf(name.Length > Appointment.ClientNameMaxLength, "clientName", "clientName.length")
            .AddIf(phone is { Length: > Appointment.ClientPhoneMaxLength }, "clientPhone", "phone.invalid");

        var location = await db.Set<Location>().AsNoTracking().SingleOrDefaultAsync(l => l.Id == request.LocationId, ct);
        var service = await db.Set<Service>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == request.ServiceId, ct);
        errors.AddIf(location is null, "locationId", "location.notFound")
              .AddIf(service is null, "serviceId", "service.notAvailable");
        if (location is not null && service is not null)
        {
            await CheckStaffAsync(db, errors, request.StaffId, request.LocationId, request.ServiceId, ct);
        }

        if (!errors.IsValid)
        {
            return errors.ToProblem();
        }

        var appointment = Appointment.BookForGuest(
            location!.Id, service!.Id, service.Name, service.Price, request.StaffId, name, phone, request.StartsAt, service.DurationMinutes);
        db.Add(appointment);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (DbErrors.IsOverlapConflict(ex))
        {
            return SchedulingEndpoints.SlotTaken("slot.taken");
        }

        // The staff member gets the usual e-mail with the .ics; there is no client e-mail for a guest.
        await events.PublishAsync(appointment, AppointmentChange.Booked, await currentUser.GetUserIdAsync(ct), ct);

        return TypedResults.Created($"/api/scheduling/calendar/appointments/{appointment.Id}", ToDto(appointment, null));
    }

    private static async Task<Results<Ok<CalendarAppointmentDto>, NotFound, ValidationProblem, ProblemHttpResult>> MoveAsync(
        Guid id, MoveCalendarAppointmentRequest request, AppDbContext db, IUserDirectory users, ICurrentUser currentUser,
        AppointmentEvents events, CancellationToken ct)
    {
        var appointment = await db.Set<Appointment>().SingleOrDefaultAsync(a => a.Id == id, ct);
        if (appointment is null)
        {
            return TypedResults.NotFound();
        }

        if (appointment.Status != AppointmentStatus.Confirmed)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "appointment.notConfirmed",
                detail: "Only confirmed appointments can be moved.");
        }

        var errors = new ValidationErrors();
        await CheckStaffAsync(db, errors, request.StaffId, appointment.LocationId, appointment.ServiceId, ct);
        if (!errors.IsValid)
        {
            return errors.ToProblem();
        }

        var previousStaffId = appointment.StaffMemberId;
        var previousStart = appointment.StartsAt;
        appointment.Reschedule(request.StaffId, request.StartsAt);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (DbErrors.IsOverlapConflict(ex))
        {
            return SchedulingEndpoints.SlotTaken("slot.taken");
        }

        if (previousStaffId != appointment.StaffMemberId || previousStart != appointment.StartsAt)
        {
            await events.PublishAsync(appointment, AppointmentChange.Rescheduled, await currentUser.GetUserIdAsync(ct), ct, previousStaffId);
        }

        return TypedResults.Ok(await ToDtoAsync(appointment, users, ct));
    }

    private static async Task<Results<Ok<CalendarAppointmentDto>, NotFound, ProblemHttpResult>> CancelAsync(
        Guid id, AppDbContext db, IUserDirectory users, ICurrentUser currentUser, AppointmentEvents events, TimeProvider clock,
        CancellationToken ct)
    {
        var appointment = await db.Set<Appointment>().SingleOrDefaultAsync(a => a.Id == id, ct);
        if (appointment is null)
        {
            return TypedResults.NotFound();
        }

        if (appointment.Status != AppointmentStatus.Confirmed)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "appointment.notConfirmed",
                detail: "The appointment is no longer active.");
        }

        // The business may cancel at any time (no client notice).
        appointment.Cancel(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(appointment, AppointmentChange.Cancelled, await currentUser.GetUserIdAsync(ct), ct);
        return TypedResults.Ok(await ToDtoAsync(appointment, users, ct));
    }

    /// <summary>
    /// F4-01: completed (with the amount charged) or no-show, once the appointment has started. Admins for any
    /// appointment, Staff for their own.
    /// </summary>
    private static async Task<Results<Ok<CalendarAppointmentDto>, NotFound, ValidationProblem, ProblemHttpResult>> OutcomeAsync(
        Guid id, AppointmentOutcomeRequest request, AppDbContext db, IUserDirectory users, ICurrentUser currentUser,
        IAuthorizationService authorization, HttpContext http, TimeProvider clock, CalendarBroadcast broadcast, CancellationToken ct)
    {
        var errors = new ValidationErrors()
            .AddIf(request.Outcome is not (AppointmentStatus.Completed or AppointmentStatus.NoShow or AppointmentStatus.Confirmed),
                "outcome", "outcome.invalid")
            .AddIf(request.ChargedAmount is < 0 or > MaxChargedAmount, "chargedAmount", "price.range");
        if (!errors.IsValid)
        {
            return errors.ToProblem();
        }

        var appointment = await db.Set<Appointment>().SingleOrDefaultAsync(a => a.Id == id, ct);
        if (appointment is null || !await CanManageAsync(appointment, db, currentUser, authorization, http, ct))
        {
            return TypedResults.NotFound();
        }

        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "appointment.notConfirmed");
        }

        if (appointment.StartsAt > clock.GetUtcNow())
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "appointment.notStarted");
        }

        appointment.RecordOutcome(request.Outcome, request.ChargedAmount);
        await db.SaveChangesAsync(ct);
        await broadcast.ChangedAsync(ct);
        return TypedResults.Ok(await ToDtoAsync(appointment, users, ct));
    }

    /// <summary>Admins manage every appointment; Staff only those of the staff record linked to them.</summary>
    private static async Task<bool> CanManageAsync(
        Appointment appointment, AppDbContext db, ICurrentUser currentUser, IAuthorizationService authorization, HttpContext http,
        CancellationToken ct)
    {
        if ((await authorization.AuthorizeAsync(http.User, null, new TenantRoleRequirement([TenantRole.Admin]))).Succeeded)
        {
            return true;
        }

        var userId = await currentUser.GetUserIdAsync(ct);
        return await db.Set<StaffMember>().AnyAsync(s => s.Id == appointment.StaffMemberId && s.UserId == userId, ct);
    }

    /// <summary>The staff member must exist, work at the location and perform the service (working hours are not enforced: Admin override).</summary>
    private static async Task CheckStaffAsync(
        AppDbContext db, ValidationErrors errors, Guid staffId, Guid locationId, Guid serviceId, CancellationToken ct)
    {
        var ok = await db.Set<StaffMember>().AnyAsync(
            s => s.Id == staffId
                 && s.Locations.Any(l => l.LocationId == locationId)
                 && s.Services.Any(x => x.ServiceId == serviceId), ct);
        errors.AddIf(!ok, "staffId", "staff.notAvailable");
    }

    private static async Task<CalendarAppointmentDto> ToDtoAsync(Appointment a, IUserDirectory users, CancellationToken ct)
    {
        UserSummary? client = null;
        if (a.ClientUserId is { } clientId)
        {
            client = (await users.GetByIdsAsync([clientId], ct)).GetValueOrDefault(clientId);
        }

        return ToDto(a, client);
    }

    private static CalendarAppointmentDto ToDto(Appointment a, UserSummary? client) => new(
        a.Id, a.LocationId, a.ServiceId, a.ServiceName, a.Price, a.StaffMemberId, a.StartsAt, a.EndsAt, a.Status,
        client?.DisplayName ?? a.ClientName, client?.Email, client?.PhoneNumber ?? a.ClientPhone, a.ClientUserId is null,
        ClientsEndpoints.KeyOf(a), a.ChargedAmount);
}
