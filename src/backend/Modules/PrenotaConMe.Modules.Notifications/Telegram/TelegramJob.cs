using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Notifications.Domain;
using PrenotaConMe.Modules.Notifications.Push;

namespace PrenotaConMe.Modules.Notifications.Telegram;

/// <summary>F3-T2: sends a message to a user's linked chat through the business's bot (no-op when not linked).</summary>
public sealed partial class TelegramJob(
    AppDbContext db, ITelegramApi api, IDataProtectionProvider protection, ILogger<TelegramJob> logger)
{
    /// <summary>Same content as the push notification, as plain text with the absolute link.</summary>
    public static string TextOf(PushNotification n, string? publicUrl) =>
        string.IsNullOrWhiteSpace(publicUrl) ? $"{n.Title}\n{n.Body}" : $"{n.Title}\n{n.Body}\n{publicUrl.TrimEnd('/')}{n.Url}";

    [AutomaticRetry(Attempts = 3)]
    public async Task SendToUserAsync(Guid userId, string text, CancellationToken cancellationToken)
    {
        var link = await db.Set<TelegramLink>().SingleOrDefaultAsync(l => l.UserId == userId, cancellationToken);
        if (link is null)
        {
            return;
        }

        var bot = await db.Set<TelegramBot>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (bot is null)
        {
            return;
        }

        var token = protection.CreateProtector(TelegramService.TokenPurpose).Unprotect(bot.ProtectedToken);
        if (await api.SendMessageAsync(token, link.ChatId, text, cancellationToken) == TelegramSendResult.Blocked)
        {
            db.Remove(link);
            await db.SaveChangesAsync(cancellationToken);
            LogBlocked(logger, userId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} blocked the Telegram bot; link removed")]
    private static partial void LogBlocked(ILogger logger, Guid userId);
}
