using PrenotaConMe.Shared.Domain;

namespace PrenotaConMe.Modules.Notifications.Domain;

/// <summary>
/// F2-06: a registered client's choice about appointment reminders from this business. Reminders are on until the
/// client turns them off (no row = on); booking an appointment is the opt-in.
/// </summary>
public sealed class ReminderPreference : TenantEntity
{
    private ReminderPreference()
    {
    }

    public Guid UserId { get; private set; }

    public bool Enabled { get; private set; }

    public static ReminderPreference Create(Guid userId, bool enabled) => new() { UserId = userId, Enabled = enabled };

    public void Set(bool enabled) => Enabled = enabled;
}
