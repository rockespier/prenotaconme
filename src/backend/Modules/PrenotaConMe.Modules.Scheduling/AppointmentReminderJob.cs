using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Shared.Notifications;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Scheduling;

/// <summary>
/// F2-06: a client reminder, scheduled when the appointment is booked or moved. It runs in the appointment's tenant
/// and re-checks everything, so moved, cancelled or past appointments and reminders the business turned off are skipped.
/// </summary>
public sealed partial class AppointmentReminderJob(
    AppDbContext db, ITenantContext tenant, ITenantDirectory tenants, AppointmentEvents events, IAppointmentNotifier notifier,
    ILogger<AppointmentReminderJob> logger)
{
    /// <param name="startsAt">Start the reminder was scheduled for; a move schedules new reminders.</param>
    /// <param name="hoursBefore">Which reminder this is; skipped when the business no longer uses it.</param>
    public async Task SendAsync(Guid appointmentId, DateTimeOffset startsAt, int hoursBefore, CancellationToken cancellationToken)
    {
        var appointment = await db.Set<Appointment>().AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);
        if (appointment is null || appointment.Status != AppointmentStatus.Confirmed || appointment.StartsAt != startsAt.ToUniversalTime())
        {
            LogSkipped(logger, appointmentId, hoursBefore);
            return;
        }

        var tenantId = tenant.TenantId!.Value;
        var business = (await tenants.GetByIdsAsync([tenantId], cancellationToken))[tenantId];
        if (!business.ReminderHours.Contains(hoursBefore))
        {
            LogSkipped(logger, appointmentId, hoursBefore);
            return;
        }

        var notice = await events.BuildNoticeAsync(appointment, cancellationToken);
        await notifier.RemindAsync(notice, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reminder ({HoursBefore} h) of appointment {AppointmentId} skipped: moved, cancelled or turned off")]
    private static partial void LogSkipped(ILogger logger, Guid appointmentId, int hoursBefore);
}
