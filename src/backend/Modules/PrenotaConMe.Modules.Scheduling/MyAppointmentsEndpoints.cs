using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Notifications;
using PrenotaConMe.Shared.Tenancy;
using PrenotaConMe.Shared.Validation;

namespace PrenotaConMe.Modules.Scheduling;

/// <param name="ChangeableUntil">Last instant the client may cancel or reschedule (start minus the business notice).</param>
/// <param name="CanChange">Confirmed and the notice deadline has not passed yet.</param>
public sealed record MyAppointmentDto(
    Guid Id, Guid LocationId, string LocationName, string? LocationAddress, string? LocationCity, string TimeZone,
    Guid ServiceId, string ServiceName, decimal Price, Guid StaffMemberId, string StaffName,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, AppointmentStatus Status,
    DateTimeOffset ChangeableUntil, bool CanChange);

public sealed record MyAppointmentsDto(int CancellationNoticeHours, IReadOnlyList<MyAppointmentDto> Appointments);

/// <param name="StaffId">null = any free staff member.</param>
public sealed record RescheduleAppointmentRequest(DateTimeOffset StartsAt, Guid? StaffId);

/// <summary>F1-12: a signed-in client's own appointments in the current business.</summary>
internal static class MyAppointmentsEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/appointments/mine", ListAsync)
            .RequireAuthorization()
            .RequireTenant()
            .WithName("GetMyAppointments");

        group.MapPost("/appointments/{id:guid}/cancel", CancelAsync)
            .RequireAuthorization()
            .RequireTenant()
            .WithName("CancelMyAppointment");

        group.MapPost("/appointments/{id:guid}/reschedule", RescheduleAsync)
            .RequireAuthorization()
            .RequireTenant()
            .WithName("RescheduleMyAppointment");
    }

    private static async Task<Ok<MyAppointmentsDto>> ListAsync(
        AppDbContext db, ICurrentUser currentUser, ITenantContext tenant, ITenantDirectory tenants,
        TimeProvider clock, CancellationToken ct)
    {
        var userId = await currentUser.GetUserIdAsync(ct);
        var notice = await NoticeHoursAsync(tenant, tenants, ct);
        var now = clock.GetUtcNow();

        var appointments = await db.Set<Appointment>().AsNoTracking()
            .Where(a => a.ClientUserId == userId)
            .OrderByDescending(a => a.StartsAt)
            .ToListAsync(ct);

        var locationIds = appointments.Select(a => a.LocationId).Distinct().ToList();
        var staffIds = appointments.Select(a => a.StaffMemberId).Distinct().ToList();
        var locations = await db.Set<Location>().AsNoTracking()
            .Where(l => locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, ct);
        var staff = await db.Set<StaffMember>().AsNoTracking()
            .Where(s => staffIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.DisplayName, ct);

        return TypedResults.Ok(new MyAppointmentsDto(
            notice,
            appointments.Select(a => ToDto(a, locations[a.LocationId], staff[a.StaffMemberId], notice, now)).ToList()));
    }

    private static async Task<Results<Ok<AppointmentDto>, NotFound, ProblemHttpResult>> CancelAsync(
        Guid id, AppDbContext db, ICurrentUser currentUser, ITenantContext tenant, ITenantDirectory tenants,
        AppointmentEvents events, TimeProvider clock, CancellationToken ct)
    {
        var appointment = await FindOwnAsync(db, currentUser, id, ct);
        if (appointment is null)
        {
            return TypedResults.NotFound();
        }

        var now = clock.GetUtcNow();
        if (await CheckChangeableAsync(appointment, tenant, tenants, now, ct) is { } refused)
        {
            return refused;
        }

        appointment.Cancel(now);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(appointment, AppointmentChange.Cancelled, appointment.ClientUserId, ct);
        return TypedResults.Ok(SchedulingEndpoints.ToDto(appointment));
    }

    private static async Task<Results<Ok<AppointmentDto>, NotFound, ValidationProblem, ProblemHttpResult>> RescheduleAsync(
        Guid id, RescheduleAppointmentRequest request, AvailabilityService availability, AppDbContext db,
        ICurrentUser currentUser, ITenantContext tenant, ITenantDirectory tenants, AppointmentEvents events, TimeProvider clock,
        CancellationToken ct)
    {
        var appointment = await FindOwnAsync(db, currentUser, id, ct);
        if (appointment is null)
        {
            return TypedResults.NotFound();
        }

        if (await CheckChangeableAsync(appointment, tenant, tenants, clock.GetUtcNow(), ct) is { } refused)
        {
            return refused;
        }

        var location = await db.Set<Location>().AsNoTracking().SingleAsync(l => l.Id == appointment.LocationId, ct);
        var date = AvailabilityService.LocalDate(request.StartsAt, location.TimeZone);
        var result = await availability.GetAsync(
            new AvailabilityQuery(appointment.LocationId, appointment.ServiceId, request.StaffId, date, date, appointment.Id), ct);
        if (result.Error is not null)
        {
            return new ValidationErrors().Add("availability", result.Error).ToProblem();
        }

        var slot = result.Slots.FirstOrDefault(s => s.StartsAt == request.StartsAt.ToUniversalTime());
        if (slot is null)
        {
            return SchedulingEndpoints.SlotTaken("slot.unavailable");
        }

        var staffId = request.StaffId
            ?? await SchedulingEndpoints.LeastBusyAsync(db, slot.StaffIds, slot.StartsAt, location.TimeZone, ct, appointment.Id);
        var previousStaffId = appointment.StaffMemberId;
        appointment.Reschedule(staffId, slot.StartsAt);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (DbErrors.IsExclusionViolation(ex))
        {
            return SchedulingEndpoints.SlotTaken("slot.taken");
        }

        await events.PublishAsync(appointment, AppointmentChange.Rescheduled, appointment.ClientUserId, ct, previousStaffId);

        return TypedResults.Ok(SchedulingEndpoints.ToDto(appointment));
    }

    /// <summary>Only the client's own appointments; anyone else's id looks like a missing one.</summary>
    private static async Task<Appointment?> FindOwnAsync(AppDbContext db, ICurrentUser currentUser, Guid id, CancellationToken ct)
    {
        var userId = await currentUser.GetUserIdAsync(ct);
        return await db.Set<Appointment>().SingleOrDefaultAsync(a => a.Id == id && a.ClientUserId == userId, ct);
    }

    /// <summary>409 when the appointment is no longer confirmed or the notice deadline has passed.</summary>
    private static async Task<ProblemHttpResult?> CheckChangeableAsync(
        Appointment appointment, ITenantContext tenant, ITenantDirectory tenants, DateTimeOffset now, CancellationToken ct)
    {
        if (appointment.Status != AppointmentStatus.Confirmed)
        {
            return Conflict("appointment.notConfirmed", "The appointment is no longer active.");
        }

        var notice = await NoticeHoursAsync(tenant, tenants, ct);
        return appointment.CanChange(now, notice)
            ? null
            : Conflict("appointment.tooLate", $"Changes are allowed up to {notice} hours before the appointment.");
    }

    private static async Task<int> NoticeHoursAsync(ITenantContext tenant, ITenantDirectory tenants, CancellationToken ct)
    {
        var id = tenant.TenantId!.Value;
        return (await tenants.GetByIdsAsync([id], ct))[id].CancellationNoticeHours;
    }

    private static ProblemHttpResult Conflict(string code, string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: code, detail: detail);

    private static MyAppointmentDto ToDto(Appointment a, Location location, string staffName, int notice, DateTimeOffset now) => new(
        a.Id, a.LocationId, location.Name, location.Address, location.City, location.TimeZone,
        a.ServiceId, a.ServiceName, a.Price, a.StaffMemberId, staffName,
        a.StartsAt, a.EndsAt, a.Status, a.ChangeableUntil(notice), a.CanChange(now, notice));
}
