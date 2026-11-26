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
    public const int HandoffReasonMaxLength = 300;

    /// <summary>Same window as WhatsApp's customer service window.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    /// <summary>Older messages are dropped (whole exchanges, from the oldest client message).</summary>
    public const int MaxTurns = 40;

    /// <summary>
    /// F3-07: client messages kept as context (with the replies and tool results between them). The model gets
    /// only the recent exchanges; the action waiting for confirmation is kept apart in <see cref="PendingAction"/>.
    /// </summary>
    public const int MaxClientMessages = 6;

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

    /// <summary>F3-07: start of the current one-minute window of <see cref="RateMinuteCount"/>.</summary>
    public DateTimeOffset RateMinuteStart { get; private set; }

    /// <summary>F3-07: messages received in the current minute, refused ones included.</summary>
    public int RateMinuteCount { get; private set; }

    /// <summary>F3-07: business-local day of <see cref="RateDayCount"/>.</summary>
    public DateOnly RateDay { get; private set; }

    /// <summary>F3-07: messages received on <see cref="RateDay"/>, refused ones included.</summary>
    public int RateDayCount { get; private set; }

    /// <summary>
    /// F3-06: when the client was handed to a person. Until an Admin resumes the assistant (or <see cref="Lifetime"/>
    /// passes) the assistant stays silent in this chat; the client's messages are still kept for the Admin to read.
    /// </summary>
    public DateTimeOffset? HandoffAt { get; private set; }

    /// <summary>F3-06: what the client needs, for the Admin.</summary>
    public string? HandoffReason { get; private set; }

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

        // F3-06: a handoff nobody closed ends by itself, even if the client keeps writing.
        if (expired || now - HandoffAt > Lifetime)
        {
            HandoffAt = null;
            HandoffReason = null;
        }

        UserId = userId;
        MessageCount++;
        LastMessageAt = now;
        turns.Add(new ChatTurn(ChatTurnRole.User, text));
        Trim(turns);
        return turns;
    }

    public void Save(IReadOnlyList<ChatTurn> turns)
    {
        var kept = turns.ToList();
        Trim(kept);
        Messages = JsonSerializer.Serialize(kept, Json);
    }

    /// <summary>
    /// F3-07: counts a received message against the per-chat limits. Over a limit, only the first refused message
    /// gets an answer (<see cref="ChatRate.TooFast"/> / <see cref="ChatRate.DailyLimit"/>); later ones are ignored.
    /// </summary>
    public ChatRate Count(DateTimeOffset now, DateOnly today, int perMinute, int perDay)
    {
        if (now - RateMinuteStart >= TimeSpan.FromMinutes(1))
        {
            RateMinuteStart = now;
            RateMinuteCount = 0;
        }

        if (RateDay != today)
        {
            RateDay = today;
            RateDayCount = 0;
        }

        RateMinuteCount++;
        RateDayCount++;
        if (RateDayCount > perDay)
        {
            return RateDayCount == perDay + 1 ? ChatRate.DailyLimit : ChatRate.Ignore;
        }

        if (RateMinuteCount > perMinute)
        {
            return RateMinuteCount == perMinute + 1 ? ChatRate.TooFast : ChatRate.Ignore;
        }

        return ChatRate.Allowed;
    }

    /// <summary>Drops whole exchanges from the oldest, cutting at a client message so tool results keep their call.</summary>
    private static void Trim(List<ChatTurn> turns)
    {
        while (turns.Count > MaxTurns || turns.Count(t => t.Role == ChatTurnRole.User) > MaxClientMessages)
        {
            var next = turns.FindIndex(1, t => t.Role == ChatTurnRole.User);
            if (next < 0)
            {
                break;
            }

            turns.RemoveRange(0, next);
        }
    }

    /// <summary>The action proposed in an earlier message, if it is the same one (the client has answered since).</summary>
    public bool IsConfirmed(string action) => PendingAction == action && PendingAt < MessageCount;

    public void Propose(string action)
    {
        PendingAction = action;
        PendingAt = MessageCount;
    }

    public void ClearPending() => PendingAction = null;

    public bool IsHandedOff => HandoffAt is not null;

    /// <summary>The handoff will end with the next message (<see cref="Receive"/> clears it).</summary>
    public bool HandoffExpired(DateTimeOffset now) => now - HandoffAt > Lifetime || now - LastMessageAt > Lifetime;

    /// <summary>F3-06: pauses the assistant in this chat; returns false when it was already paused.</summary>
    public bool Handoff(string reason, DateTimeOffset now)
    {
        if (IsHandedOff)
        {
            return false;
        }

        reason = reason.Trim();
        HandoffAt = now;
        HandoffReason = reason.Length > HandoffReasonMaxLength ? reason[..HandoffReasonMaxLength] : reason;
        PendingAction = null;
        return true;
    }

    /// <summary>F3-06: an Admin turned the assistant back on; it starts with a clean history.</summary>
    public void Resume()
    {
        HandoffAt = null;
        HandoffReason = null;
        PendingAction = null;
        Messages = "[]";
    }
}

/// <summary>F3-07: outcome of <see cref="ChatConversation.Count"/>.</summary>
public enum ChatRate
{
    Allowed,
    TooFast,
    DailyLimit,
    Ignore,
}
