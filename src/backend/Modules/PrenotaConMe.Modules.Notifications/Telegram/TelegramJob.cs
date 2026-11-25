using System.Globalization;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Notifications.Domain;
using PrenotaConMe.Modules.Notifications.Push;
using PrenotaConMe.Shared.Chatbot;

namespace PrenotaConMe.Modules.Notifications.Telegram;

/// <summary>F3-T2: sends a message to a user's linked chat through the business's bot (no-op when not linked).</summary>
public sealed partial class TelegramJob(
    AppDbContext db, ITelegramApi api, IDataProtectionProvider protection, IChatbot chatbot, ISpeechToText speech,
    ILogger<TelegramJob> logger)
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

    /// <summary>
    /// F3-03: a client's message to the business bot, answered by the booking assistant. Not retried: the
    /// assistant may already have booked, and it answers errors with an apology itself.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task ChatAsync(Guid userId, long chatId, string text, CancellationToken cancellationToken)
    {
        var bot = await db.Set<TelegramBot>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (bot is null)
        {
            return;
        }

        var reply = await chatbot.ReplyAsync(
            new ChatbotMessage("telegram", chatId.ToString(CultureInfo.InvariantCulture), userId, text), cancellationToken);
        var token = protection.CreateProtector(TelegramService.TokenPurpose).Unprotect(bot.ProtectedToken);
        await api.SendMessageAsync(token, chatId, reply, cancellationToken);
    }

    /// <summary>
    /// F3-04: a voice message, transcribed and then answered like a text. When the audio cannot be read the client
    /// is asked to write instead (the chatbot replies in the business language).
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task VoiceAsync(Guid userId, long chatId, string fileId, CancellationToken cancellationToken)
    {
        var bot = await db.Set<TelegramBot>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (bot is null)
        {
            return;
        }

        var token = protection.CreateProtector(TelegramService.TokenPurpose).Unprotect(bot.ProtectedToken);
        string? text = null;
        try
        {
            var audio = await api.DownloadFileAsync(token, fileId, cancellationToken);
            using var stream = new MemoryStream(audio);
            text = await speech.TranscribeAsync(stream, "voice.ogg", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogVoiceFailed(logger, ex, chatId);
        }

        // Without a transcript the chatbot still answers (it asks to repeat in writing).
        var reply = await chatbot.ReplyAsync(
            new ChatbotMessage("telegram", chatId.ToString(CultureInfo.InvariantCulture), userId, text ?? ChatbotMessage.Unintelligible),
            cancellationToken);
        await api.SendMessageAsync(token, chatId, reply, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Voice message in Telegram chat {ChatId} could not be transcribed")]
    private static partial void LogVoiceFailed(ILogger logger, Exception exception, long chatId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} blocked the Telegram bot; link removed")]
    private static partial void LogBlocked(ILogger logger, Guid userId);
}
