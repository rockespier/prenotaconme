using System.Collections.Concurrent;
using System.Text.Json;
using PrenotaConMe.Modules.Notifications.Telegram;

namespace PrenotaConMe.IntegrationTests;

/// <summary>Replaces the Telegram Bot API. Tokens starting with "valid" are accepted; chat ids below 0 have blocked the bot.</summary>
public sealed class FakeTelegramApi : ITelegramApi
{
    public sealed record SentMessage(string Token, long ChatId, string Text);

    public ConcurrentQueue<SentMessage> Sent { get; } = new();

    /// <summary>Last setWebhook per token: (url, secret).</summary>
    public ConcurrentDictionary<string, (string Url, string Secret)> Webhooks { get; } = new();

    public Task<TelegramBotInfo?> GetMeAsync(string token, CancellationToken cancellationToken) =>
        Task.FromResult(token.StartsWith("valid", StringComparison.Ordinal)
            ? new TelegramBotInfo(Math.Abs(token.GetHashCode(StringComparison.Ordinal)), "bot_" + token[5..])
            : null);

    public Task SetWebhookAsync(string token, string url, string secret, CancellationToken cancellationToken)
    {
        Webhooks[token] = (url, secret);
        return Task.CompletedTask;
    }

    public Task DeleteWebhookAsync(string token, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<JsonElement>> GetUpdatesAsync(string token, long offset, int timeoutSeconds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<JsonElement>>([]);

    public Task<TelegramSendResult> SendMessageAsync(string token, long chatId, string text, CancellationToken cancellationToken)
    {
        if (chatId < 0)
        {
            return Task.FromResult(TelegramSendResult.Blocked);
        }

        Sent.Enqueue(new SentMessage(token, chatId, text));
        return Task.FromResult(TelegramSendResult.Sent);
    }

    public Task SendTypingAsync(string token, long chatId, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Bytes of a voice file: its id as UTF-8 (the fake transcriber reads it back).</summary>
    public Task<byte[]> DownloadFileAsync(string token, string fileId, CancellationToken cancellationToken) =>
        Task.FromResult(System.Text.Encoding.UTF8.GetBytes(fileId));

    /// <summary>
    /// Waits up to 30 s: a job enqueued while the Hangfire server is still starting can miss the LISTEN/NOTIFY
    /// wake-up and wait for the next queue poll (15 s).
    /// </summary>
    public async Task<IReadOnlyList<SentMessage>> WaitForAsync(long chatId, int count)
    {
        for (var i = 0; i < 600; i++)
        {
            var found = Sent.Where(m => m.ChatId == chatId).ToList();
            if (found.Count >= count)
            {
                return found;
            }

            await Task.Delay(50);
        }

        return Sent.Where(m => m.ChatId == chatId).ToList();
    }
}
