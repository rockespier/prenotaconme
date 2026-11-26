namespace PrenotaConMe.Shared.Notifications;

/// <summary>
/// F3-06: a client on a chat channel asked for a person (or the assistant could not help). Captured by the chatbot
/// so the notification side never has to query other modules.
/// </summary>
/// <param name="Reason">Short summary of what the client needs (from the assistant, or the client's own words).</param>
public sealed record HandoffAlert(
    string BusinessName,
    string BusinessSlug,
    string Language,
    string? ClientName,
    string? ClientPhone,
    string? ClientEmail,
    string Channel,
    string Reason);

/// <summary>Messages for the business's own people (implemented by the Notifications module).</summary>
public interface IStaffAlerts
{
    /// <summary>
    /// F3-06: queues a push, Telegram and e-mail message to every Admin of the current business. Returns at once and
    /// never throws: the handoff itself is already saved.
    /// </summary>
    void Handoff(HandoffAlert alert);
}
