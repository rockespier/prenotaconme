namespace PrenotaConMe.Shared.Chatbot;

/// <summary>
/// A message a client sent to the business on a chat channel, already tied to their account.
/// </summary>
/// <param name="Channel">"telegram" now, "whatsapp" later: the engine only uses it to keep conversations apart.</param>
/// <param name="ConversationId">The channel's id of the chat (Telegram chat id, WhatsApp phone number).</param>
/// <param name="UserId">Local user linked to the chat: bookings are made for them.</param>
public sealed record ChatbotMessage(string Channel, string ConversationId, Guid UserId, string Text)
{
    /// <summary>F3-04: sent as the text of a voice message that could not be transcribed.</summary>
    public const string Unintelligible = "[voice message that could not be understood]";
}

/// <summary>
/// F3-03: the booking assistant (implemented by the Chatbot module). Channel-agnostic: the channel delivers the
/// message in the business's tenant and sends the returned text back.
/// </summary>
public interface IChatbot
{
    /// <summary>False when no AI provider is configured (OpenAI:ApiKey): channels then only send notifications.</summary>
    bool IsEnabled { get; }

    /// <summary>The reply to send. Never throws for model or booking errors: it returns an apology instead.</summary>
    Task<string> ReplyAsync(ChatbotMessage message, CancellationToken cancellationToken);
}
