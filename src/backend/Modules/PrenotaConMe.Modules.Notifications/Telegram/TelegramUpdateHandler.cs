using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Notifications.Domain;

namespace PrenotaConMe.Modules.Notifications.Telegram;

/// <summary>
/// F3-T2: messages people send to the business's bot (webhook or polling, already in the bot's tenant).
/// "/start &lt;token&gt;" links the chat to the user who created the token; "/stop" unlinks it.
/// Booking by chat arrives with the chatbot (F3-03).
/// </summary>
internal sealed class TelegramUpdateHandler(AppDbContext db, ITelegramApi api, TelegramService telegram, TimeProvider clock)
{
    public async Task HandleAsync(TelegramBot bot, TelegramUpdate update, CancellationToken ct)
    {
        if (update.ChatId is not { } chatId || string.IsNullOrWhiteSpace(update.Text))
        {
            return;
        }

        var business = await telegram.BusinessAsync(ct);
        var texts = TelegramTexts.For(business.DefaultLanguage);
        var myBookings = telegram.PublicUrl($"/{business.Slug}/prenotazioni");
        var text = update.Text.Trim();
        string reply;

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            var code = text["/start".Length..].Trim();
            if (code.Length == 0)
            {
                reply = TelegramTexts.Format(texts.Welcome, business.Name, myBookings);
            }
            else if (await LinkAsync(code, chatId, ct))
            {
                reply = TelegramTexts.Format(texts.Linked, business.Name, null);
            }
            else
            {
                reply = TelegramTexts.Format(texts.InvalidLink, business.Name, myBookings);
            }
        }
        else if (text.StartsWith("/stop", StringComparison.OrdinalIgnoreCase))
        {
            await db.Set<TelegramLink>().Where(l => l.ChatId == chatId).ExecuteDeleteAsync(ct);
            reply = TelegramTexts.Format(texts.Unlinked, business.Name, null);
        }
        else
        {
            reply = TelegramTexts.Format(texts.NotYet, business.Name, telegram.PublicUrl($"/{business.Slug}"));
        }

        await api.SendMessageAsync(telegram.Unprotect(bot), chatId, reply, ct);
    }

    /// <summary>One chat per user and one user per chat: an older link of either side is replaced.</summary>
    private async Task<bool> LinkAsync(string code, long chatId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var token = await db.Set<TelegramLinkToken>().SingleOrDefaultAsync(t => t.Token == code, ct);
        if (token is null || token.ExpiresAt <= now)
        {
            return false;
        }

        await db.Set<TelegramLink>().Where(l => l.ChatId == chatId || l.UserId == token.UserId).ExecuteDeleteAsync(ct);
        db.Add(TelegramLink.Create(token.UserId, chatId));
        db.Remove(token);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
