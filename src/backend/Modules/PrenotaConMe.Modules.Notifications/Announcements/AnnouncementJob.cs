using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Notifications.Domain;
using PrenotaConMe.Modules.Notifications.Push;
using PrenotaConMe.Modules.Notifications.Telegram;
using PrenotaConMe.Modules.Users.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Notifications.Announcements;

/// <param name="Name">Display name, for the greeting.</param>
public sealed record AnnouncementRecipient(Guid UserId, string? Email, string? Name);

/// <summary>
/// F2-03: sends an announcement to every client of the business (users with the Client role). Runs in the
/// tenant that queued it; fans out one e-mail job per address and one push job per user, so retries stay local.
/// </summary>
public sealed partial class AnnouncementJob(
    AppDbContext db, ITenantContext tenant, ITenantDirectory tenants, IBackgroundJobClient jobs, IEmailSender sender,
    IOptions<NotificationOptions> options, IOptions<PushOptions> push, ILogger<AnnouncementJob> logger)
{
    /// <summary>Clients of the current business (also used to show the audience size before sending).</summary>
    public static async Task<List<AnnouncementRecipient>> RecipientsAsync(AppDbContext db, CancellationToken ct) =>
        await db.Set<UserMembership>()
            .Where(m => m.Role == TenantRole.Client)
            .Join(db.Set<UserAccount>(), m => m.UserId, u => u.Id, (m, u) => new AnnouncementRecipient(u.Id, u.Email, u.DisplayName))
            .Distinct()
            .ToListAsync(ct);

    public async Task SendAsync(Guid announcementId, CancellationToken cancellationToken)
    {
        var announcement = await db.Set<Announcement>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == announcementId, cancellationToken);
        if (announcement is null)
        {
            return;
        }

        var business = (await tenants.GetByIdsAsync([tenant.TenantId!.Value], cancellationToken))[tenant.TenantId!.Value];
        var recipients = await RecipientsAsync(db, cancellationToken);
        var notification = new PushNotification(
            announcement.Title, $"{business.Name} · {announcement.Message}", $"/{business.Slug}", $"announcement-{announcement.Id}");
        var telegramText = TelegramJob.TextOf(notification with { Body = announcement.Message }, options.Value.PublicUrl);

        foreach (var r in recipients)
        {
            if (!string.IsNullOrWhiteSpace(r.Email))
            {
                var to = r;
                jobs.Enqueue<AnnouncementJob>(j => j.SendEmailAsync(announcementId, to, CancellationToken.None));
            }

            var userId = r.UserId;
            if (push.Value.Enabled)
            {
                jobs.Enqueue<PushJob>(j => j.SendToUserAsync(userId, notification, CancellationToken.None));
            }

            jobs.Enqueue<TelegramJob>(j => j.SendToUserAsync(userId, telegramText, CancellationToken.None));
        }

        LogQueued(logger, announcementId, recipients.Count);
    }

    [AutomaticRetry(Attempts = 5)]
    public async Task SendEmailAsync(Guid announcementId, AnnouncementRecipient recipient, CancellationToken cancellationToken)
    {
        var announcement = await db.Set<Announcement>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == announcementId, cancellationToken);
        if (announcement is null || string.IsNullOrWhiteSpace(recipient.Email))
        {
            return;
        }

        var business = (await tenants.GetByIdsAsync([tenant.TenantId!.Value], cancellationToken))[tenant.TenantId!.Value];
        var publicUrl = options.Value.PublicUrl?.TrimEnd('/');
        var message = AnnouncementEmails.Build(
            recipient.Email, recipient.Name, business.Name, business.DefaultLanguage, announcement.Title, announcement.Message,
            string.IsNullOrWhiteSpace(publicUrl) ? null : $"{publicUrl}/{business.Slug}");
        await sender.SendAsync(message, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Announcement {AnnouncementId} queued for {Recipients} clients")]
    private static partial void LogQueued(ILogger logger, Guid announcementId, int recipients);
}
