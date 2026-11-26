using PrenotaConMe.Shared.Domain;

namespace PrenotaConMe.Modules.Chatbot.Domain;

/// <summary>
/// F3-07: language-model use of one business on one day (business-local date). Checked against the daily budget
/// before each model call and incremented atomically after it (<see cref="ChatbotEngine"/>), so parallel chats of
/// the same business never lose an update.
/// </summary>
public sealed class ChatUsage : TenantEntity
{
    private ChatUsage()
    {
    }

    public DateOnly Day { get; private set; }

    /// <summary>Client messages answered by the model (quick replies and refused messages are not counted).</summary>
    public int Messages { get; private set; }

    public long InputTokens { get; private set; }

    public long OutputTokens { get; private set; }

    /// <summary>Seconds of voice messages transcribed (speech-to-text is billed per minute).</summary>
    public long AudioSeconds { get; private set; }

    public long Tokens => InputTokens + OutputTokens;
}
