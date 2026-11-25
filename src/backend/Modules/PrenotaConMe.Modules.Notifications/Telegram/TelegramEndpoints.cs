using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Notifications.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Tenancy;
using PrenotaConMe.Shared.Validation;

namespace PrenotaConMe.Modules.Notifications.Telegram;

/// <param name="Username">Bot username without "@"; null when the business has no bot.</param>
public sealed record TelegramBotDto(string? Username, TelegramMode Mode);

public sealed record SaveTelegramBotRequest(string Token);

/// <param name="BotUsername">Null when the business has no bot (nothing to link).</param>
public sealed record TelegramLinkDto(string? BotUsername, bool Linked);

/// <param name="Url">https://t.me/&lt;bot&gt;?start=&lt;one-time code&gt;, valid 30 minutes.</param>
public sealed record TelegramLinkUrlDto(string Url);

/// <summary>F3-T1 (business bot) and F3-T2 (linking people's chats to their account).</summary>
internal static class TelegramEndpoints
{
    public const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/telegram/bot", GetBotAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("GetTelegramBot");

        group.MapPut("/telegram/bot", SaveBotAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("SaveTelegramBot");

        group.MapDelete("/telegram/bot", DeleteBotAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("DeleteTelegramBot");

        group.MapGet("/telegram/link", GetLinkAsync)
            .RequireTenant()
            .RequireAuthorization()
            .WithName("GetTelegramLink");

        group.MapPost("/telegram/link", CreateLinkAsync)
            .RequireTenant()
            .RequireAuthorization()
            .WithName("CreateTelegramLink");

        group.MapDelete("/telegram/link", DeleteLinkAsync)
            .RequireTenant()
            .RequireAuthorization()
            .WithName("DeleteTelegramLink");

        group.MapPost("/telegram/webhook/{botId:guid}", WebhookAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();
    }

    private static async Task<Ok<TelegramBotDto>> GetBotAsync(TelegramService telegram, IOptions<TelegramOptions> options, CancellationToken ct) =>
        TypedResults.Ok(new TelegramBotDto((await telegram.CurrentBotAsync(ct))?.Username, options.Value.Mode));

    /// <summary>Checks the token with Telegram (getMe), stores it encrypted and points the bot at this app.</summary>
    private static async Task<Results<Ok<TelegramBotDto>, ValidationProblem, ProblemHttpResult>> SaveBotAsync(
        SaveTelegramBotRequest request, AppDbContext db, TelegramService telegram, ITelegramApi api,
        IOptions<TelegramOptions> options, CancellationToken ct)
    {
        var token = request.Token?.Trim() ?? string.Empty;
        var info = await api.GetMeAsync(token, ct);
        if (info is null)
        {
            return new ValidationErrors().Add("token", "telegram.token.invalid").ToProblem();
        }

        string? webhookUrl = null;
        if (options.Value.Mode == TelegramMode.Webhook)
        {
            webhookUrl = telegram.PublicUrl("/api/notifications/telegram/webhook/");
            if (webhookUrl is null || !webhookUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "telegram.webhookUnavailable",
                    detail: "Webhook mode needs Notifications:PublicUrl with https.");
            }
        }

        var bot = await telegram.CurrentBotAsync(ct);
        if (bot is null)
        {
            bot = TelegramBot.Create(info.Id, info.Username, telegram.Protect(token));
            db.Add(bot);
        }
        else
        {
            if (bot.BotUserId != info.Id)
            {
                // Another bot: people must start it again before it may write to them.
                await db.Set<TelegramLink>().ExecuteDeleteAsync(ct);
            }

            bot.Replace(info.Id, info.Username, telegram.Protect(token));
        }

        await db.SaveChangesAsync(ct);

        if (webhookUrl is not null)
        {
            await api.SetWebhookAsync(token, webhookUrl + bot.Id, bot.WebhookSecret, ct);
        }
        else
        {
            await api.DeleteWebhookAsync(token, ct);
        }

        return TypedResults.Ok(new TelegramBotDto(bot.Username, options.Value.Mode));
    }

    private static async Task<NoContent> DeleteBotAsync(AppDbContext db, TelegramService telegram, ITelegramApi api, CancellationToken ct)
    {
        var bot = await telegram.CurrentBotAsync(ct);
        if (bot is not null)
        {
            try
            {
                await api.DeleteWebhookAsync(telegram.Unprotect(bot), ct);
            }
            catch (HttpRequestException)
            {
                // Revoked token or Telegram unreachable: removing it here must still work.
            }

            await db.Set<TelegramLink>().ExecuteDeleteAsync(ct);
            await db.Set<TelegramLinkToken>().ExecuteDeleteAsync(ct);
            db.Remove(bot);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Ok<TelegramLinkDto>> GetLinkAsync(
        AppDbContext db, TelegramService telegram, ICurrentUser currentUser, CancellationToken ct)
    {
        var bot = await telegram.CurrentBotAsync(ct);
        if (bot is null)
        {
            return TypedResults.Ok(new TelegramLinkDto(null, false));
        }

        var userId = await currentUser.GetUserIdAsync(ct);
        return TypedResults.Ok(new TelegramLinkDto(bot.Username, await db.Set<TelegramLink>().AnyAsync(l => l.UserId == userId, ct)));
    }

    private static async Task<Results<Ok<TelegramLinkUrlDto>, ProblemHttpResult>> CreateLinkAsync(
        AppDbContext db, TelegramService telegram, ICurrentUser currentUser, TimeProvider clock, CancellationToken ct)
    {
        var bot = await telegram.CurrentBotAsync(ct);
        if (bot is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "telegram.notConfigured",
                detail: "This business has no Telegram bot.");
        }

        var userId = await currentUser.GetUserIdAsync(ct);
        var now = clock.GetUtcNow();
        await db.Set<TelegramLinkToken>().Where(t => t.UserId == userId || t.ExpiresAt <= now).ExecuteDeleteAsync(ct);
        var token = TelegramLinkToken.Create(userId, now);
        db.Add(token);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new TelegramLinkUrlDto($"https://t.me/{bot.Username}?start={token.Token}"));
    }

    private static async Task<NoContent> DeleteLinkAsync(AppDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var userId = await currentUser.GetUserIdAsync(ct);
        await db.Set<TelegramLink>().Where(l => l.UserId == userId).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Called by Telegram (webhook mode). The bot id in the path picks the business; the secret header proves the
    /// call comes from Telegram. Always 200 once authenticated, so Telegram does not retry an update forever.
    /// </summary>
    private static async Task<Results<Ok, UnauthorizedHttpResult>> WebhookAsync(
        Guid botId, [FromBody] JsonElement update, [FromHeader(Name = SecretHeader)] string? secret,
        AppDbContext db, TenantContext tenant, TelegramUpdateHandler handler, CancellationToken ct)
    {
        var bot = await db.Set<TelegramBot>().IgnoreQueryFilters([AppDbContext.TenantFilter]).AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == botId, ct);
        if (bot is null || secret is null
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(bot.WebhookSecret)))
        {
            return TypedResults.Unauthorized();
        }

        tenant.SetTenant(bot.TenantId);
        await handler.HandleAsync(bot, TelegramUpdate.Parse(update), ct);
        return TypedResults.Ok();
    }
}
