using System.Globalization;
using System.Net;
using System.Text;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Notifications.Push;
using PrenotaConMe.Modules.Notifications.Telegram;
using PrenotaConMe.Modules.Users.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Notifications;

namespace PrenotaConMe.Modules.Notifications.Handoff;

/// <summary>F3-06: queues <see cref="HandoffJob"/>; enqueueing must not fail the chat reply.</summary>
internal sealed partial class StaffAlerts(IBackgroundJobClient jobs, ILogger<StaffAlerts> logger) : IStaffAlerts
{
    public void Handoff(HandoffAlert alert)
    {
        try
        {
            jobs.Enqueue<HandoffJob>(j => j.SendAsync(alert, CancellationToken.None));
        }
        catch (Exception ex)
        {
            LogEnqueueFailed(logger, ex, alert.BusinessSlug);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not queue the handoff alert for {Business}")]
    private static partial void LogEnqueueFailed(ILogger logger, Exception exception, string business);
}

/// <summary>
/// F3-06: tells every Admin of the business that a client wants a person: one push and one Telegram job per Admin
/// and one e-mail job per address, so retries stay local. The link opens the backoffice requests page.
/// </summary>
public sealed class HandoffJob(
    AppDbContext db, IBackgroundJobClient jobs, IEmailSender sender, IOptions<NotificationOptions> options,
    IOptions<PushOptions> push)
{
    public const string PagePath = "/admin/conversazioni";

    private sealed record Texts(string Title, string Phone, string Email, string Intro, string Reason, string Paused, string Open);

    private static readonly Dictionary<string, Texts> ByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["it"] = new(
            "Richiesta di contatto da {0}", "Telefono", "Email",
            "{0} ha chiesto di parlare con una persona nella chat di {1} ({2}).", "Motivo",
            "L'assistente automatico è in pausa in quella chat finché non lo riattivi.", "Apri le richieste"),
        ["es"] = new(
            "Solicitud de contacto de {0}", "Teléfono", "Email",
            "{0} ha pedido hablar con una persona en el chat de {1} ({2}).", "Motivo",
            "El asistente automático está en pausa en ese chat hasta que lo reactives.", "Abrir las solicitudes"),
        ["en"] = new(
            "Contact request from {0}", "Phone", "E-mail",
            "{0} asked to talk to a person in the {1} chat ({2}).", "Reason",
            "The automatic assistant is paused in that chat until you turn it back on.", "Open the requests"),
    };

    private static readonly Dictionary<string, string> UnknownClient = new(StringComparer.OrdinalIgnoreCase)
    {
        ["it"] = "un cliente", ["es"] = "un cliente", ["en"] = "a client",
    };

    public async Task SendAsync(HandoffAlert alert, CancellationToken cancellationToken)
    {
        var t = ByLanguage.GetValueOrDefault(alert.Language) ?? ByLanguage["it"];
        var client = alert.ClientName ?? UnknownClient.GetValueOrDefault(alert.Language) ?? UnknownClient["it"];
        var body = alert.ClientPhone is null ? alert.Reason : $"{alert.Reason} · {t.Phone} {alert.ClientPhone}";
        var notification = new PushNotification(Format(t.Title, client), body, PagePath, $"handoff-{Guid.NewGuid():N}");
        var telegramText = TelegramJob.TextOf(notification, options.Value.PublicUrl);

        var admins = await db.Set<UserMembership>()
            .Where(m => m.Role == TenantRole.Admin)
            .Join(db.Set<UserAccount>(), m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.Email, u.DisplayName })
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var admin in admins)
        {
            var userId = admin.Id;
            if (push.Value.Enabled)
            {
                jobs.Enqueue<PushJob>(j => j.SendToUserAsync(userId, notification, CancellationToken.None));
            }

            jobs.Enqueue<TelegramJob>(j => j.SendToUserAsync(userId, telegramText, CancellationToken.None));
            if (!string.IsNullOrWhiteSpace(admin.Email))
            {
                var (to, name) = (admin.Email, admin.DisplayName);
                jobs.Enqueue<HandoffJob>(j => j.SendEmailAsync(alert, to, name, CancellationToken.None));
            }
        }
    }

    [AutomaticRetry(Attempts = 5)]
    public Task SendEmailAsync(HandoffAlert alert, string to, string? toName, CancellationToken cancellationToken) =>
        sender.SendAsync(BuildEmail(alert, to, toName, options.Value.PublicUrl), cancellationToken);

    internal static EmailMessage BuildEmail(HandoffAlert alert, string to, string? toName, string? publicUrl)
    {
        var t = ByLanguage.GetValueOrDefault(alert.Language) ?? ByLanguage["it"];
        var client = alert.ClientName ?? UnknownClient.GetValueOrDefault(alert.Language) ?? UnknownClient["it"];
        var channel = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(alert.Channel);
        var link = string.IsNullOrWhiteSpace(publicUrl) ? null : publicUrl.TrimEnd('/') + PagePath;
        var lines = new List<(string Label, string Value)> { (t.Reason, alert.Reason) };
        if (alert.ClientPhone is not null)
        {
            lines.Add((t.Phone, alert.ClientPhone));
        }

        if (alert.ClientEmail is not null)
        {
            lines.Add((t.Email, alert.ClientEmail));
        }

        var intro = Format(t.Intro, client, alert.BusinessName, channel);
        var text = new StringBuilder().AppendLine(intro).AppendLine();
        foreach (var (label, value) in lines)
        {
            text.Append(label).Append(": ").AppendLine(value);
        }

        text.AppendLine().AppendLine(t.Paused);
        if (link is not null)
        {
            text.Append(t.Open).Append(": ").AppendLine(link);
        }

        var html = new StringBuilder()
            .Append("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:15px;color:#222;max-width:560px\">")
            .Append("<p>").Append(Encode(intro)).Append("</p><p>");
        foreach (var (label, value) in lines)
        {
            html.Append("<strong>").Append(Encode(label)).Append(":</strong> ").Append(Encode(value)).Append("<br>");
        }

        html.Append("</p><p>").Append(Encode(t.Paused)).Append("</p>");
        if (link is not null)
        {
            html.Append("<p><a href=\"").Append(Encode(link)).Append("\" style=\"color:#1a5fb4\">").Append(Encode(t.Open)).Append("</a></p>");
        }

        html.Append("</div>");
        return new EmailMessage(to, toName, alert.BusinessName, Format(t.Title, client), text.ToString(), html.ToString(), []);
    }

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
