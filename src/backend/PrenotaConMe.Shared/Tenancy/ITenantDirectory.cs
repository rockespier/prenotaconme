namespace PrenotaConMe.Shared.Tenancy;

/// <param name="DefaultLanguage">ISO 639-1 code (it/es/en) used for messages to clients and staff.</param>
/// <param name="CancellationNoticeHours">Minimum notice for a client to cancel or reschedule.</param>
/// <param name="TimeZone">IANA id of the business (local dates such as announcement validity).</param>
/// <param name="FirstReminderHours">F2-06: hours before an appointment of the first client reminder (0 = off).</param>
/// <param name="SecondReminderHours">F2-06: hours before of the second reminder (0 = off).</param>
public sealed record TenantSummary(
    Guid Id, string Slug, string Name, bool IsActive, string DefaultLanguage, int CancellationNoticeHours, string TimeZone,
    int FirstReminderHours, int SecondReminderHours, bool GoogleCalendarSync = false)
{
    /// <summary>The reminders that are on, distinct, earliest first (largest number of hours).</summary>
    public IReadOnlyList<int> ReminderHours =>
        new[] { FirstReminderHours, SecondReminderHours }.Where(h => h > 0).Distinct().OrderDescending().ToList();
}

/// <summary>Read-only lookup of tenants for other modules (implemented by the Tenancy module).</summary>
public interface ITenantDirectory
{
    Task<TenantSummary?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, TenantSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
