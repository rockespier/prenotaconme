using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace PrenotaConMe.Modules.Notifications;

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// <param name="FromName">Display name of the sender (the business); the address is always <see cref="SmtpOptions.From"/>.</param>
public sealed record EmailMessage(
    string To, string? ToName, string FromName, string Subject, string TextBody, string HtmlBody,
    IReadOnlyList<EmailAttachment> Attachments);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public enum SmtpSecurity
{
    Auto,
    None,
    StartTls,
    SslOnConnect,
}

/// <summary>"Smtp" configuration section. The password belongs in user-secrets / the server's shared settings.</summary>
public sealed class SmtpOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    public string? User { get; set; }

    public string? Password { get; set; }

    public string From { get; set; } = string.Empty;
}

/// <summary>"Notifications" configuration section.</summary>
public sealed class NotificationOptions
{
    /// <summary>For testing: when set, every e-mail meant for staff goes to this address instead.</summary>
    public string? StaffEmail { get; set; }

    /// <summary>Public site URL (e.g. https://prenotaconme.rtres.net), for links to the booking pages in e-mails.</summary>
    public string? PublicUrl { get; set; }
}

internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var smtp = options.Value;

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(message.FromName, smtp.From));
        mime.To.Add(new MailboxAddress(message.ToName ?? string.Empty, message.To));
        mime.Subject = message.Subject;

        var body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody };
        foreach (var attachment in message.Attachments)
        {
            body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        mime.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(smtp.Host!, smtp.Port, ToSocketOptions(smtp.Security), cancellationToken);
        if (!string.IsNullOrEmpty(smtp.User))
        {
            await client.AuthenticateAsync(smtp.User, smtp.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    private static SecureSocketOptions ToSocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.None => SecureSocketOptions.None,
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.Auto,
    };
}

/// <summary>Used when no SMTP host is configured (tests, fresh dev machines): only logs that a message would be sent.</summary>
internal sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        LogSkipped(logger, message.Subject);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SMTP not configured; e-mail not sent: {Subject}")]
    private static partial void LogSkipped(ILogger logger, string subject);
}
