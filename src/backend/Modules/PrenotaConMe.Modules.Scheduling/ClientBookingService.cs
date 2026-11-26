using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Notifications;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Scheduling;

public enum BookingErrorKind
{
    /// <summary>Bad input or data that cannot be booked (400).</summary>
    Invalid,

    /// <summary>The appointment does not exist or is not the client's (404).</summary>
    NotFound,

    /// <summary>The time is no longer free, or the appointment can no longer be changed (409).</summary>
    Conflict,
}

/// <param name="Code">Stable error key (e.g. slot.taken, appointment.tooLate) the frontend and the chatbot translate.</param>
/// <param name="Field">For <see cref="BookingErrorKind.Invalid"/>: the request field the code belongs to.</param>
public sealed record BookingError(BookingErrorKind Kind, string Code, string? Field = null, string? Detail = null);

public sealed record BookingResult(Appointment? Appointment, BookingError? Error)
{
    public static BookingResult Fail(BookingErrorKind kind, string code, string? field = null, string? detail = null) =>
        new(null, new BookingError(kind, code, field, detail));
}

/// <summary>
/// What a client can do with their own appointments (book, cancel, move), for a given client user: the web
/// endpoints pass the signed-in user, the chatbot the user linked to the chat. Runs in the current tenant.
/// </summary>
public sealed class ClientBookingService(
    AppDbContext db, AvailabilityService availability, IMembershipWriter memberships, ITenantContext tenant,
    ITenantDirectory tenants, AppointmentEvents events, TimeProvider clock)
{
    /// <summary>F1-09: the start must be one of the slots offered for that local day.</summary>
    public async Task<BookingResult> BookAsync(Guid clientId, BookAppointmentRequest request, CancellationToken ct)
    {
        var location = await db.Set<Location>().AsNoTracking().SingleOrDefaultAsync(l => l.Id == request.LocationId, ct);
        if (location is null)
        {
            return BookingResult.Fail(BookingErrorKind.Invalid, "location.notFound", "locationId");
        }

        var date = AvailabilityService.LocalDate(request.StartsAt, location.TimeZone);
        var result = await availability.GetAsync(new AvailabilityQuery(request.LocationId, request.ServiceId, request.StaffId, date, date), ct);
        if (result.Error is not null)
        {
            return BookingResult.Fail(BookingErrorKind.Invalid, result.Error, "availability");
        }

        var slot = result.Slots.FirstOrDefault(s => s.StartsAt == request.StartsAt.ToUniversalTime());
        if (slot is null)
        {
            return SlotTaken("slot.unavailable");
        }

        var staffId = request.StaffId ?? await SchedulingEndpoints.LeastBusyAsync(db, slot.StaffIds, request.StartsAt, location.TimeZone, ct);
        var service = result.Service!;

        var appointment = Appointment.Book(
            request.LocationId, service.Id, service.Name, service.Price, staffId, clientId, slot.StartsAt, service.DurationMinutes);
        db.Add(appointment);
        await memberships.EnsureAsync(tenant.TenantId!.Value, clientId, TenantRole.Client, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (DbErrors.IsOverlapConflict(ex))
        {
            // Someone booked the same staff member at an overlapping time in the meantime.
            db.Entry(appointment).State = EntityState.Detached;
            return SlotTaken("slot.taken");
        }

        // F1-11 / F2-02: confirmation e-mails and push notifications are sent in the background.
        await events.PublishAsync(appointment, AppointmentChange.Booked, clientId, ct);
        return new BookingResult(appointment, null);
    }

    /// <summary>F1-12: only the client's own, confirmed appointments, before the business notice.</summary>
    public async Task<BookingResult> CancelAsync(Guid clientId, Guid appointmentId, CancellationToken ct)
    {
        var appointment = await FindOwnAsync(clientId, appointmentId, ct);
        if (appointment is null)
        {
            return BookingResult.Fail(BookingErrorKind.NotFound, "appointment.notFound");
        }

        var now = clock.GetUtcNow();
        if (await CheckChangeableAsync(appointment, now, ct) is { } refused)
        {
            return refused;
        }

        appointment.Cancel(now);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(appointment, AppointmentChange.Cancelled, clientId, ct);
        return new BookingResult(appointment, null);
    }

    /// <summary>F1-12: the appointment's own time counts as free while looking for the new one.</summary>
    public async Task<BookingResult> RescheduleAsync(Guid clientId, Guid appointmentId, RescheduleAppointmentRequest request, CancellationToken ct)
    {
        var appointment = await FindOwnAsync(clientId, appointmentId, ct);
        if (appointment is null)
        {
            return BookingResult.Fail(BookingErrorKind.NotFound, "appointment.notFound");
        }

        if (await CheckChangeableAsync(appointment, clock.GetUtcNow(), ct) is { } refused)
        {
            return refused;
        }

        var location = await db.Set<Location>().AsNoTracking().SingleAsync(l => l.Id == appointment.LocationId, ct);
        var date = AvailabilityService.LocalDate(request.StartsAt, location.TimeZone);
        var result = await availability.GetAsync(
            new AvailabilityQuery(appointment.LocationId, appointment.ServiceId, request.StaffId, date, date, appointment.Id), ct);
        if (result.Error is not null)
        {
            return BookingResult.Fail(BookingErrorKind.Invalid, result.Error, "availability");
        }

        var slot = result.Slots.FirstOrDefault(s => s.StartsAt == request.StartsAt.ToUniversalTime());
        if (slot is null)
        {
            return SlotTaken("slot.unavailable");
        }

        var staffId = request.StaffId
            ?? await SchedulingEndpoints.LeastBusyAsync(db, slot.StaffIds, slot.StartsAt, location.TimeZone, ct, appointment.Id);
        var previousStaffId = appointment.StaffMemberId;
        var previousStart = appointment.StartsAt;
        appointment.Reschedule(staffId, slot.StartsAt);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (DbErrors.IsOverlapConflict(ex))
        {
            appointment.Reschedule(previousStaffId, previousStart);
            db.Entry(appointment).State = EntityState.Unchanged;
            return SlotTaken("slot.taken");
        }

        await events.PublishAsync(appointment, AppointmentChange.Rescheduled, clientId, ct, previousStaffId);
        return new BookingResult(appointment, null);
    }

    /// <summary>Minimum notice (hours) the business asks for cancelling or moving.</summary>
    public async Task<int> NoticeHoursAsync(CancellationToken ct)
    {
        var id = tenant.TenantId!.Value;
        return (await tenants.GetByIdsAsync([id], ct))[id].CancellationNoticeHours;
    }

    /// <summary>Only the client's own appointments; anyone else's id looks like a missing one.</summary>
    private Task<Appointment?> FindOwnAsync(Guid clientId, Guid id, CancellationToken ct) =>
        db.Set<Appointment>().SingleOrDefaultAsync(a => a.Id == id && a.ClientUserId == clientId, ct);

    /// <summary>Refused when the appointment is no longer confirmed or the notice deadline has passed.</summary>
    private async Task<BookingResult?> CheckChangeableAsync(Appointment appointment, DateTimeOffset now, CancellationToken ct)
    {
        if (appointment.Status != AppointmentStatus.Confirmed)
        {
            return BookingResult.Fail(BookingErrorKind.Conflict, "appointment.notConfirmed", detail: "The appointment is no longer active.");
        }

        var notice = await NoticeHoursAsync(ct);
        return appointment.CanChange(now, notice)
            ? null
            : BookingResult.Fail(BookingErrorKind.Conflict, "appointment.tooLate",
                detail: $"Changes are allowed up to {notice} hours before the appointment.");
    }

    private static BookingResult SlotTaken(string code) =>
        BookingResult.Fail(BookingErrorKind.Conflict, code, detail: "The selected time is no longer available.");
}
