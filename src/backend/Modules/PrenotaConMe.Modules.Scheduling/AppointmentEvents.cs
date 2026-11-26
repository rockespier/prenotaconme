using Hangfire;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Calendar;
using PrenotaConMe.Shared.Notifications;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Scheduling;

/// <summary>
/// Builds the <see cref="AppointmentNotice"/> of a saved appointment and hands it to the notifier; schedules the
/// client reminders (F2-06) when an appointment is booked or moved.
/// </summary>
public sealed class AppointmentEvents(
    AppDbContext db, ITenantContext tenant, ITenantDirectory tenants, IUserDirectory users, IAppointmentNotifier notifier,
    IAppointmentCalendarSync calendar, IBackgroundJobClient jobs, TimeProvider clock)
{
    /// <param name="actorUserId">Who made the change; they get no push about it.</param>
    /// <param name="previousStaffId">When a move changed the staff member, the previous one is told it is no longer theirs.</param>
    public async Task PublishAsync(
        Appointment appointment, AppointmentChange change, Guid? actorUserId, CancellationToken ct, Guid? previousStaffId = null)
    {
        var (notice, business, staff) = await BuildAsync(appointment, previousStaffId, ct);
        notifier.Notify(change, notice, actorUserId);
        calendar.Changed(appointment.Id);

        if (previousStaffId is { } previousId && previousId != appointment.StaffMemberId
            && staff.TryGetValue(previousId, out var previous))
        {
            // Only the previous staff member: the client already gets the "moved" message.
            notifier.Notify(AppointmentChange.Cancelled,
                notice with { StaffName = previous.DisplayName, StaffEmail = previous.Email, StaffUserId = previous.UserId, ClientUserId = null },
                actorUserId);
        }

        if (change is AppointmentChange.Booked or AppointmentChange.Rescheduled)
        {
            ScheduleReminders(appointment, business);
        }
    }

    /// <summary>The notice of an appointment as it is now (used by the reminder job).</summary>
    public async Task<AppointmentNotice> BuildNoticeAsync(Appointment appointment, CancellationToken ct) =>
        (await BuildAsync(appointment, null, ct)).Notice;

    /// <summary>
    /// One delayed job per reminder still in the future. Moves and cancellations need no clean-up: the job checks
    /// the appointment again when it runs and does nothing when it is cancelled or no longer starts at the same time.
    /// </summary>
    private void ScheduleReminders(Appointment appointment, TenantSummary business)
    {
        if (appointment.ClientUserId is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        foreach (var hours in business.ReminderHours)
        {
            var at = appointment.StartsAt.AddHours(-hours);
            if (at > now)
            {
                var (id, startsAt) = (appointment.Id, appointment.StartsAt);
                jobs.Schedule<AppointmentReminderJob>(j => j.SendAsync(id, startsAt, hours, CancellationToken.None), at);
            }
        }
    }

    private async Task<(AppointmentNotice Notice, TenantSummary Business, Dictionary<Guid, StaffInfo> Staff)> BuildAsync(
        Appointment appointment, Guid? previousStaffId, CancellationToken ct)
    {
        var tenantId = tenant.TenantId!.Value;
        var business = (await tenants.GetByIdsAsync([tenantId], ct))[tenantId];
        var location = await db.Set<Location>().AsNoTracking().SingleAsync(l => l.Id == appointment.LocationId, ct);
        var staffIds = new[] { appointment.StaffMemberId, previousStaffId ?? appointment.StaffMemberId };
        var staff = await db.Set<StaffMember>().AsNoTracking()
            .Where(s => staffIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => new StaffInfo(s.DisplayName, s.Email, s.UserId), ct);

        UserSummary? client = null;
        if (appointment.ClientUserId is { } clientId)
        {
            client = (await users.GetByIdsAsync([clientId], ct)).GetValueOrDefault(clientId);
        }

        var current = staff[appointment.StaffMemberId];
        var notice = new AppointmentNotice(
            appointment.Id, business.Name, business.Slug, business.DefaultLanguage, appointment.ServiceName, appointment.Price,
            appointment.StartsAt, appointment.EndsAt, location.TimeZone,
            location.Name, location.Address, location.City, location.Phone,
            current.DisplayName, current.Email, current.UserId,
            client?.DisplayName ?? appointment.ClientName, client?.Email, appointment.ClientUserId);
        return (notice, business, staff);
    }

    private sealed record StaffInfo(string DisplayName, string? Email, Guid? UserId);
}
