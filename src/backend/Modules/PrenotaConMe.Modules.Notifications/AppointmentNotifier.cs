using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrenotaConMe.Modules.Notifications.Push;
using PrenotaConMe.Shared.Notifications;

namespace PrenotaConMe.Modules.Notifications;

/// <summary>
/// Queues durable jobs: one e-mail job per recipient (bookings only), so a retry after an SMTP failure never
/// re-sends the other e-mail, and one push job per user to notify. Enqueueing must not fail the change,
/// which is already committed.
/// </summary>
internal sealed partial class AppointmentNotifier(
    IBackgroundJobClient jobs, IOptions<PushOptions> push, ILogger<AppointmentNotifier> logger) : IAppointmentNotifier
{
    public void Notify(AppointmentChange change, AppointmentNotice notice, Guid? actorUserId)
    {
        try
        {
            if (change == AppointmentChange.Booked)
            {
                jobs.Enqueue<AppointmentEmailJob>(j => j.SendAsync(notice, EmailRecipient.Client, CancellationToken.None));
                jobs.Enqueue<AppointmentEmailJob>(j => j.SendAsync(notice, EmailRecipient.Staff, CancellationToken.None));
            }

            if (!push.Value.Enabled)
            {
                return;
            }

            if (notice.ClientUserId is { } clientId && clientId != actorUserId)
            {
                var message = PushMessages.Build(change, notice, PushAudience.Client);
                jobs.Enqueue<PushJob>(j => j.SendToUserAsync(clientId, message, CancellationToken.None));
            }

            if (notice.StaffUserId is { } staffId && staffId != actorUserId)
            {
                var message = PushMessages.Build(change, notice, PushAudience.Staff);
                jobs.Enqueue<PushJob>(j => j.SendToUserAsync(staffId, message, CancellationToken.None));
            }
        }
        catch (Exception ex)
        {
            LogEnqueueFailed(logger, ex, change, notice.AppointmentId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not queue the {Change} messages for appointment {AppointmentId}")]
    private static partial void LogEnqueueFailed(ILogger logger, Exception exception, AppointmentChange change, Guid appointmentId);
}
