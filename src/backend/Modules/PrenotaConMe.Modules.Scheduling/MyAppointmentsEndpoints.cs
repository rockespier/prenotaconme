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

    private static async Task<Results<Ok<AppointmentDto>, ProblemHttpResult>> CancelAsync(
        Guid id, ClientBookingService booking, ICurrentUser currentUser, CancellationToken ct)
    {
        var result = await booking.CancelAsync(await currentUser.GetUserIdAsync(ct), id, ct);
        return result.Error is { } error
            ? SchedulingEndpoints.Problem(error)
            : TypedResults.Ok(SchedulingEndpoints.ToDto(result.Appointment!));
    }

    private static async Task<Results<Ok<AppointmentDto>, ValidationProblem, ProblemHttpResult>> RescheduleAsync(
        Guid id, RescheduleAppointmentRequest request, ClientBookingService booking, ICurrentUser currentUser, CancellationToken ct)
    {
        var result = await booking.RescheduleAsync(await currentUser.GetUserIdAsync(ct), id, request, ct);
        if (result.Error is { } error)
        {
            return error.Kind == BookingErrorKind.Invalid ? SchedulingEndpoints.Invalid(error) : SchedulingEndpoints.Problem(error);
        }

        return TypedResults.Ok(SchedulingEndpoints.ToDto(result.Appointment!));
    }

    private static async Task<int> NoticeHoursAsync(ITenantContext tenant, ITenantDirectory tenants, CancellationToken ct)
    {
        var id = tenant.TenantId!.Value;
        return (await tenants.GetByIdsAsync([id], ct))[id].CancellationNoticeHours;
    }

    private static MyAppointmentDto ToDto(Appointment a, Location location, string staffName, int notice, DateTimeOffset now) => new(
        a.Id, a.LocationId, location.Name, location.Address, location.City, location.TimeZone,
        a.ServiceId, a.ServiceName, a.Price, a.StaffMemberId, staffName,
        a.StartsAt, a.EndsAt, a.Status, a.ChangeableUntil(notice), a.CanChange(now, notice));
}
