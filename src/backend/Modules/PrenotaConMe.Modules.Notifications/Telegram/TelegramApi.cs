using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrenotaConMe.Modules.Notifications.Telegram;

public sealed record TelegramBotInfo(long Id, string Username);

/// <summary>A voice message: the file is downloaded with getFile.</summary>
public sealed record TelegramVoice(string FileId, int DurationSeconds);

/// <summary>The parts of a Telegram update the app uses (private text and voice messages).</summary>
public sealed record TelegramUpdate(long UpdateId, long? ChatId, string? Text, TelegramVoice? Voice = null)
{
    public static TelegramUpdate Parse(JsonElement update)
    {
        var id = update.TryGetProperty("update_id", out var u) ? u.GetInt64() : 0;
        if (!update.TryGetProperty("message", out var message))
        {
            return new TelegramUpdate(id, null, null);
        }

        long? chatId = message.TryGetProperty("chat", out var chat) && chat.TryGetProperty("id", out var c) ? c.GetInt64() : null;
        var text = message.TryGetProperty("text", out var t) ? t.GetString() : null;
        TelegramVoice? voice = null;
        if (message.TryGetProperty("voice", out var v) && v.TryGetProperty("file_id", out var fileId))
        {
            voice = new TelegramVoice(fileId.GetString()!, v.TryGetProperty("duration", out var d) ? d.GetInt32() : 0);
        }

        return new TelegramUpdate(id, chatId, text, voice);
    }
}

public enum TelegramSendResult
{
    Sent,

    /// <summary>The user blocked the bot or deleted the chat (403): stop writing to this chat.</summary>
    Blocked,
}

/// <summary>The Telegram Bot API methods the app calls (https://core.telegram.org/bots/api).</summary>
public interface ITelegramApi
{
    /// <summary>Null when Telegram rejects the token.</summary>
    Task<TelegramBotInfo?> GetMeAsync(string token, CancellationToken cancellationToken);

    Task SetWebhookAsync(string token, string url, string secret, CancellationToken cancellationToken);

    Task DeleteWebhookAsync(string token, CancellationToken cancellationToken);

    Task<IReadOnlyList<JsonElement>> GetUpdatesAsync(string token, long offset, int timeoutSeconds, CancellationToken cancellationToken);

    Task<TelegramSendResult> SendMessageAsync(string token, long chatId, string text, CancellationToken cancellationToken);

    /// <summary>Shows "typing…" in the chat for a few seconds (best effort, never throws).</summary>
    Task SendTypingAsync(string token, long chatId, CancellationToken cancellationToken);

    /// <summary>F3-04: downloads a file sent to the bot (getFile, then the file URL; bots can download up to 20 MB).</summary>
    Task<byte[]> DownloadFileAsync(string token, string fileId, CancellationToken cancellationToken);
}

/// <summary>
/// Plain HttpClient implementation. The token is part of the URL, so this client's request logging is removed
/// and its requests are excluded from tracing (see the Api's observability setup).
/// </summary>
internal sealed class HttpTelegramApi(HttpClient http) : ITelegramApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string[] MessageUpdates = ["message"];

    public async Task<TelegramBotInfo?> GetMeAsync(string token, CancellationToken cancellationToken)
    {
        if (!LooksLikeToken(token))
        {
            return null;
        }

        var (status, result) = await CallAsync(token, "getMe", new { }, cancellationToken);
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureOk(status, "getMe");
        return new TelegramBotInfo(result.GetProperty("id").GetInt64(), result.GetProperty("username").GetString()!);
    }

    public async Task SetWebhookAsync(string token, string url, string secret, CancellationToken cancellationToken)
    {
        var (status, _) = await CallAsync(token, "setWebhook",
            new { url, secret_token = secret, allowed_updates = MessageUpdates, drop_pending_updates = true }, cancellationToken);
        EnsureOk(status, "setWebhook");
    }

    public async Task DeleteWebhookAsync(string token, CancellationToken cancellationToken)
    {
        var (status, _) = await CallAsync(token, "deleteWebhook", new { }, cancellationToken);
        EnsureOk(status, "deleteWebhook");
    }

    public async Task<IReadOnlyList<JsonElement>> GetUpdatesAsync(string token, long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var (status, result) = await CallAsync(token, "getUpdates",
            new { offset, timeout = timeoutSeconds, allowed_updates = MessageUpdates }, cancellationToken);
        EnsureOk(status, "getUpdates");
        return result.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    public async Task<TelegramSendResult> SendMessageAsync(string token, long chatId, string text, CancellationToken cancellationToken)
    {
        var (status, _) = await CallAsync(token, "sendMessage",
            new { chat_id = chatId, text, link_preview_options = new { is_disabled = true } }, cancellationToken);
        if (status == HttpStatusCode.Forbidden)
        {
            return TelegramSendResult.Blocked;
        }

        EnsureOk(status, "sendMessage");
        return TelegramSendResult.Sent;
    }

    public async Task SendTypingAsync(string token, long chatId, CancellationToken cancellationToken)
    {
        try
        {
            await CallAsync(token, "sendChatAction", new { chat_id = chatId, action = "typing" }, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            // Only a hint for the user.
        }
    }

    public async Task<byte[]> DownloadFileAsync(string token, string fileId, CancellationToken cancellationToken)
    {
        var (status, result) = await CallAsync(token, "getFile", new { file_id = fileId }, cancellationToken);
        EnsureOk(status, "getFile");
        var path = result.GetProperty("file_path").GetString()!;

        using var response = await http.GetAsync(new Uri($"https://api.telegram.org/file/bot{token}/{path}"), cancellationToken);
        EnsureOk(response.StatusCode, "file download");
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<(HttpStatusCode Status, JsonElement Result)> CallAsync(
        string token, string method, object body, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(new Uri($"https://api.telegram.org/bot{token}/{method}"), body, Json, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var result = json.RootElement.TryGetProperty("result", out var r) ? r.Clone() : default;
        return (response.StatusCode, result);
    }

    /// <summary>Never put the token (it is in the URL) in the exception.</summary>
    private static void EnsureOk(HttpStatusCode status, string method)
    {
        if ((int)status is < 200 or >= 300)
        {
            throw new HttpRequestException($"Telegram {method} failed with {(int)status}.", null, status);
        }
    }

    /// <summary>"123456:ABC-…": avoids sending garbage (or a URL) as the path.</summary>
    private static bool LooksLikeToken(string token) =>
        token.Length is > 20 and < 100 && token.Contains(':', StringComparison.Ordinal)
        && token.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '_' or '-');
}
