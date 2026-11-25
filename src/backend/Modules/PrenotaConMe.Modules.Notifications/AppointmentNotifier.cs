using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrenotaConMe.Modules.Notifications.Push;
using PrenotaConMe.Modules.Notifications.Telegram;
using PrenotaConMe.Shared.Notifications;

namespace PrenotaConMe.Modules.Notifications;

/// <summary>
/// Queues durable jobs: one e-mail job per recipient (bookings only), so a retry after an SMTP failure never
/// re-sends the other e-mail, and one push job per user to notify. Enqueueing must not fail the change,
/// which is already committed.
/// </summary>
internal sealed partial class AppointmentNotifier(
    IBackgroundJobClient jobs, IOptions<PushOptions> push, IOptions<NotificationOptions> options, ILogger<AppointmentNotifier> logger)
    : IAppointmentNotifier
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

            if (notice.ClientUserId is { } clientId && clientId != actorUserId)
            {
                Message(clientId, PushMessages.Build(change, notice, PushAudience.Client));
            }

            if (notice.StaffUserId is { } staffId && staffId != actorUserId)
            {
                Message(staffId, PushMessages.Build(change, notice, PushAudience.Staff));
            }
        }
        catch (Exception ex)
        {
            LogEnqueueFailed(logger, ex, change, notice.AppointmentId);
        }
    }

    /// <summary>Push (when configured) and Telegram (the job does nothing when the user has not linked a chat).</summary>
    private void Message(Guid userId, PushNotification message)
    {
        if (push.Value.Enabled)
        {
            jobs.Enqueue<PushJob>(j => j.SendToUserAsync(userId, message, CancellationToken.None));
        }

        var text = TelegramJob.TextOf(message, options.Value.PublicUrl);
        jobs.Enqueue<TelegramJob>(j => j.SendToUserAsync(userId, text, CancellationToken.None));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not queue the {Change} messages for appointment {AppointmentId}")]
    private static partial void LogEnqueueFailed(ILogger logger, Exception exception, AppointmentChange change, Guid appointmentId);
}
