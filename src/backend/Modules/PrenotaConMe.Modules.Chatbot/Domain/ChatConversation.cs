using System.Text.Json;
using PrenotaConMe.Shared.Domain;

namespace PrenotaConMe.Modules.Chatbot.Domain;

/// <summary>
/// F3-03/F3-05: one chat with the business's assistant. Keeps the recent messages (as JSON) so the model has
/// context, and the booking action waiting for the client's "yes". Starts over after 24 h of silence.
/// </summary>
public sealed class ChatConversation : TenantEntity
{
    public const int ChannelMaxLength = 20;
    public const int ConversationIdMaxLength = 64;
    public const int PendingActionMaxLength = 1000;

    /// <summary>Same window as WhatsApp's customer service window.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    /// <summary>Older messages are dropped (whole exchanges, from the oldest client message).</summary>
    public const int MaxTurns = 40;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ChatConversation()
    {
    }

    public string Channel { get; private set; } = string.Empty;

    public string ConversationId { get; private set; } = string.Empty;

    public Guid UserId { get; private set; }

    /// <summary>JSON array of <see cref="ChatTurn"/>.</summary>
    public string Messages { get; private set; } = "[]";

    /// <summary>Client messages so far; tells "the client answered after the question" apart from the same message.</summary>
    public int MessageCount { get; private set; }

    /// <summary>JSON of the booking action shown to the client for confirmation.</summary>
    public string? PendingAction { get; private set; }

    /// <summary><see cref="MessageCount"/> when <see cref="PendingAction"/> was proposed.</summary>
    public int PendingAt { get; private set; }

    public DateTimeOffset LastMessageAt { get; private set; }

    public static ChatConversation Start(string channel, string conversationId, Guid userId) =>
        new() { Channel = channel, ConversationId = conversationId, UserId = userId };

    public IReadOnlyList<ChatTurn> ReadMessages() => JsonSerializer.Deserialize<List<ChatTurn>>(Messages, Json) ?? [];

    /// <summary>
    /// Records a new client message: a conversation idle for longer than <see cref="Lifetime"/> (or now linked to
    /// another user) starts over.
    /// </summary>
    public List<ChatTurn> Receive(Guid userId, string text, DateTimeOffset now)
    {
        var expired = now - LastMessageAt > Lifetime || userId != UserId;
        var turns = expired ? [] : ReadMessages().ToList();
        if (expired)
        {
            PendingAction = null;
        }

        UserId = userId;
        MessageCount++;
        LastMessageAt = now;
        turns.Add(new ChatTurn(ChatTurnRole.User, text));
        return turns;
    }

    public void Save(IReadOnlyList<ChatTurn> turns)
    {
        var kept = turns.ToList();
        while (kept.Count > MaxTurns)
        {
            // Cut at the next client message so tool results never lose the call they answer.
            var next = kept.FindIndex(1, t => t.Role == ChatTurnRole.User);
            if (next < 0)
            {
                break;
            }

            kept.RemoveRange(0, next);
        }

        Messages = JsonSerializer.Serialize(kept, Json);
    }

    /// <summary>The action proposed in an earlier message, if it is the same one (the client has answered since).</summary>
    public bool IsConfirmed(string action) => PendingAction == action && PendingAt < MessageCount;

    public void Propose(string action)
    {
        PendingAction = action;
        PendingAt = MessageCount;
    }

    public void ClearPending() => PendingAction = null;
}
