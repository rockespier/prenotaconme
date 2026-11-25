using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Notifications.Domain;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Notifications.Telegram;

public enum TelegramMode
{
    /// <summary>The app asks Telegram for updates (local development: no public URL needed).</summary>
    Polling,

    /// <summary>Telegram calls /api/notifications/telegram/webhook/{botId} (needs Notifications:PublicUrl over https).</summary>
    Webhook,
}

/// <summary>"Telegram" configuration section.</summary>
public sealed class TelegramOptions
{
    public TelegramMode Mode { get; set; } = TelegramMode.Polling;
}

/// <summary>Bot tokens in and out of their encrypted form, and the URLs the bot sends people to.</summary>
internal sealed class TelegramService(
    AppDbContext db, IDataProtectionProvider protection, ITenantContext tenant, ITenantDirectory tenants,
    IOptions<NotificationOptions> options)
{
    public const string TokenPurpose = "PrenotaConMe.Telegram.BotToken";

    private readonly IDataProtector _protector = protection.CreateProtector(TokenPurpose);

    public string Protect(string token) => _protector.Protect(token);

    public string Unprotect(TelegramBot bot) => _protector.Unprotect(bot.ProtectedToken);

    /// <summary>The bot of the current business, if any.</summary>
    public Task<TelegramBot?> CurrentBotAsync(CancellationToken ct) => db.Set<TelegramBot>().SingleOrDefaultAsync(ct);

    public async Task<TenantSummary> BusinessAsync(CancellationToken ct)
    {
        var id = tenant.TenantId!.Value;
        return (await tenants.GetByIdsAsync([id], ct))[id];
    }

    /// <summary>Absolute URL of a site path, or null when Notifications:PublicUrl is not set.</summary>
    public string? PublicUrl(string path) =>
        string.IsNullOrWhiteSpace(options.Value.PublicUrl) ? null : options.Value.PublicUrl.TrimEnd('/') + path;
}
