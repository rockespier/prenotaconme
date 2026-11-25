namespace PrenotaConMe.Shared.Tenancy;

/// <param name="DefaultLanguage">ISO 639-1 code (it/es/en) used for messages to clients and staff.</param>
/// <param name="CancellationNoticeHours">Minimum notice for a client to cancel or reschedule.</param>
/// <param name="TimeZone">IANA id of the business (local dates such as announcement validity).</param>
public sealed record TenantSummary(
    Guid Id, string Slug, string Name, bool IsActive, string DefaultLanguage, int CancellationNoticeHours, string TimeZone);

/// <summary>Read-only lookup of tenants for other modules (implemented by the Tenancy module).</summary>
public interface ITenantDirectory
{
    Task<TenantSummary?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, TenantSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
