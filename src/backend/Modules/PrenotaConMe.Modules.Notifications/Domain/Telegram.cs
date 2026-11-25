using System.Security.Cryptography;
using PrenotaConMe.Shared.Domain;

namespace PrenotaConMe.Modules.Notifications.Domain;

/// <summary>F3-T1: the business's Telegram bot (created with @BotFather). One per business.</summary>
public sealed class TelegramBot : TenantEntity
{
    private TelegramBot()
    {
    }

    /// <summary>Telegram's id of the bot (the part of the token before ':').</summary>
    public long BotUserId { get; private set; }

    public string Username { get; private set; } = string.Empty;

    /// <summary>Bot token encrypted with ASP.NET Data Protection.</summary>
    public string ProtectedToken { get; private set; } = string.Empty;

    /// <summary>Telegram sends it back in X-Telegram-Bot-Api-Secret-Token on every webhook call.</summary>
    public string WebhookSecret { get; private set; } = string.Empty;

    public static TelegramBot Create(long botUserId, string username, string protectedToken) => new()
    {
        BotUserId = botUserId,
        Username = username,
        ProtectedToken = protectedToken,
        WebhookSecret = NewSecret(),
    };

    public void Replace(long botUserId, string username, string protectedToken)
    {
        BotUserId = botUserId;
        Username = username;
        ProtectedToken = protectedToken;
        WebhookSecret = NewSecret();
    }

    private static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}

/// <summary>F3-T2: a user of the business who receives messages from its bot in this Telegram chat.</summary>
public sealed class TelegramLink : TenantEntity
{
    private TelegramLink()
    {
    }

    public Guid UserId { get; private set; }

    public long ChatId { get; private set; }

    public static TelegramLink Create(Guid userId, long chatId) => new() { UserId = userId, ChatId = chatId };
}

/// <summary>One-time code of the deep link t.me/&lt;bot&gt;?start=&lt;token&gt; that links a chat to the user who asked for it.</summary>
public sealed class TelegramLinkToken : TenantEntity
{
    public const int TokenLength = 32;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private TelegramLinkToken()
    {
    }

    /// <summary>Base64url, so it fits Telegram's start parameter (A-Z, a-z, 0-9, _ and -, max 64).</summary>
    public string Token { get; private set; } = string.Empty;

    public Guid UserId { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public static TelegramLinkToken Create(Guid userId, DateTimeOffset now) => new()
    {
        Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_'),
        UserId = userId,
        ExpiresAt = now + Lifetime,
    };
}
