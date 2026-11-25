using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Chatbot.Domain;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Chatbot;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Chatbot;

/// <summary>
/// F3-03: answers a client message with the language model and the booking tools. Messages of one conversation
/// are handled one at a time (Postgres advisory lock), so two quick messages never race on the same history.
/// </summary>
internal sealed partial class ChatbotEngine(
    AppDbContext db, IChatModel model, BookingTools tools, ClientBookingService booking, ITenantContext tenant,
    ITenantDirectory tenants, IUserDirectory users, IOptions<ChatbotOptions> options, TimeProvider clock,
    ILogger<ChatbotEngine> logger) : IChatbot
{
    /// <summary>Model calls per client message (each may run tools); stops runaway loops.</summary>
    public const int MaxRounds = 6;

    public const int MaxMessageLength = 1000;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public async Task<string> ReplyAsync(ChatbotMessage message, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId!.Value;
        var business = (await tenants.GetByIdsAsync([tenantId], cancellationToken))[tenantId];
        var texts = ChatbotTexts.For(business.DefaultLanguage);

        await db.Database.OpenConnectionAsync(cancellationToken);
        var key = LockKey(tenantId, message.Channel, message.ConversationId);
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({key})", cancellationToken);
            return await ReplyLockedAsync(message, business, texts, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, message.Channel, message.ConversationId);
            return texts.Error;
        }
        finally
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({key})", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<string> ReplyLockedAsync(ChatbotMessage message, TenantSummary business, ChatbotTexts texts, CancellationToken ct)
    {
        var conversation = await db.Set<ChatConversation>()
            .SingleOrDefaultAsync(c => c.Channel == message.Channel && c.ConversationId == message.ConversationId, ct);
        if (conversation is null)
        {
            conversation = ChatConversation.Start(message.Channel, message.ConversationId, message.UserId);
            db.Add(conversation);
        }

        var text = message.Text.Trim();
        var turns = conversation.Receive(message.UserId, text.Length > MaxMessageLength ? text[..MaxMessageLength] : text, clock.GetUtcNow());
        var prompt = await SystemPromptAsync(business, message.UserId, ct);

        string? reply = null;
        for (var round = 0; round < MaxRounds && reply is null; round++)
        {
            var answer = await model.CompleteAsync(prompt, turns, BookingTools.Definitions, ct);
            if (answer.ToolCalls.Count == 0)
            {
                reply = answer.Text ?? texts.NotUnderstood;
                break;
            }

            turns.Add(new ChatTurn(ChatTurnRole.Assistant, answer.Text, answer.ToolCalls));
            foreach (var call in answer.ToolCalls)
            {
                var result = await tools.ExecuteAsync(call, conversation, business.DefaultLanguage, ct);
                turns.Add(new ChatTurn(ChatTurnRole.Tool, result, ToolCallId: call.Id));
            }
        }

        reply ??= texts.NotUnderstood;
        turns.Add(new ChatTurn(ChatTurnRole.Assistant, reply));
        conversation.Save(turns);
        await db.SaveChangesAsync(ct);
        return reply;
    }

    private async Task<string> SystemPromptAsync(TenantSummary business, Guid userId, CancellationToken ct)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZone);
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timeZone);
        var client = (await users.GetByIdsAsync([userId], ct)).GetValueOrDefault(userId);
        var phones = await db.Set<Location>().AsNoTracking()
            .Where(l => l.IsActive && l.Phone != null)
            .Select(l => l.Name + ": " + l.Phone)
            .ToListAsync(ct);
        var notice = await booking.NoticeHoursAsync(ct);

        return string.Join("\n", new[]
        {
            $"You are the booking assistant of \"{business.Name}\", chatting with a client.",
            $"Today is {now.ToString("dddd yyyy-MM-dd", CultureInfo.InvariantCulture)}, the local time is {now:HH:mm} ({business.TimeZone}). All times are local to the business.",
            client is null ? null : $"The client's name is {client.DisplayName}.",
            $"Reply in the client's language; if unsure, use {ChatbotTexts.LanguageName(business.DefaultLanguage)}.",
            "Be brief and friendly, like a receptionist on a chat app. Plain text only: no Markdown, no tables. Never show ids.",
            "Only help with appointments at this business: services, prices, free times, booking, moving and cancelling. Politely decline anything else.",
            "Never invent services, prices, staff or times: always use the tools. Offer a few free times, not the whole list.",
            $"Client messages may be transcribed from voice: allow for small transcription errors. If a message is \"{ChatbotMessage.Unintelligible}\", ask the client to repeat it or write it.",
            "Interpret relative dates (tomorrow, next Friday, afternoon = from 14:00) from today's date.",
            "Booking, moving and cancelling ask for confirmation: show the summary the tool returns, ask the client to confirm, and call the tool again with the same arguments only after a clear yes.",
            $"Clients can move or cancel up to {notice} hours before the appointment.",
            phones.Count == 0 ? null : $"If you cannot help, suggest calling the business: {string.Join("; ", phones)}.",
        }.Where(l => l is not null));
    }

    /// <summary>64-bit key of the advisory lock of one conversation.</summary>
    private static long LockKey(Guid tenantId, string channel, string conversationId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId:N}|{channel}|{conversationId}"));
        return BitConverter.ToInt64(hash, 0);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Chatbot could not answer on {Channel} conversation {ConversationId}")]
    private static partial void LogFailed(ILogger logger, Exception exception, string channel, string conversationId);
}

/// <summary>Fixed replies when the model cannot answer, in the business's default language.</summary>
internal sealed record ChatbotTexts(string Error, string NotUnderstood)
{
    private static readonly Dictionary<string, ChatbotTexts> ByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["it"] = new("Scusa, in questo momento non riesco a rispondere. Riprova tra qualche minuto.", "Scusa, non ho capito. Puoi riformulare?"),
        ["es"] = new("Lo siento, ahora mismo no puedo responder. Inténtalo de nuevo en unos minutos.", "Perdona, no te he entendido. ¿Puedes decirlo de otra forma?"),
        ["en"] = new("Sorry, I cannot answer right now. Please try again in a few minutes.", "Sorry, I did not understand. Could you rephrase?"),
    };

    public static ChatbotTexts For(string language) => ByLanguage.GetValueOrDefault(language) ?? ByLanguage["it"];

    public static string LanguageName(string language) => language switch
    {
        "es" => "Spanish",
        "en" => "English",
        _ => "Italian",
    };
}
