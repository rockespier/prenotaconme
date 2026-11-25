using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrenotaConMe.Shared.Notifications;

namespace PrenotaConMe.Modules.Notifications;

public enum EmailRecipient
{
    Client,
    Staff,

    /// <summary>F2-06: reminder to the client before the appointment.</summary>
    ClientReminder,
}

/// <summary>F1-11 confirmation and F2-06 reminder e-mails, run by Hangfire: SMTP failures are retried with back-off.</summary>
public sealed partial class AppointmentEmailJob(
    IEmailSender sender, IOptions<NotificationOptions> options, TimeProvider clock, ILogger<AppointmentEmailJob> logger)
{
    [AutomaticRetry(Attempts = 5)]
    public async Task SendAsync(AppointmentNotice notice, EmailRecipient recipient, CancellationToken cancellationToken)
    {
        EmailMessage? message;
        try
        {
            message = recipient switch
            {
                EmailRecipient.Client => AppointmentEmails.ForClient(notice, clock.GetUtcNow()),
                EmailRecipient.ClientReminder => AppointmentEmails.ForClient(notice, clock.GetUtcNow(), reminder: true),
                _ => AppointmentEmails.ForStaff(notice, options.Value.StaffEmail, clock.GetUtcNow()),
            };
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or FormatException)
        {
            // Bad data: retrying would not help.
            LogBuildFailed(logger, ex, notice.AppointmentId);
            return;
        }

        if (message is null)
        {
            return;
        }

        await sender.SendAsync(message, cancellationToken);
        LogSent(logger, recipient, notice.AppointmentId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Appointment e-mail ({Recipient}) sent for appointment {AppointmentId}")]
    private static partial void LogSent(ILogger logger, EmailRecipient recipient, Guid appointmentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not build the appointment e-mail for appointment {AppointmentId}")]
    private static partial void LogBuildFailed(ILogger logger, Exception exception, Guid appointmentId);
}
