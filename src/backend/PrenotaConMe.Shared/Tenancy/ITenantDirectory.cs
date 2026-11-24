namespace PrenotaConMe.Shared.Tenancy;

/// <param name="DefaultLanguage">ISO 639-1 code (it/es/en) used for messages to clients and staff.</param>
public sealed record TenantSummary(Guid Id, string Slug, string Name, bool IsActive, string DefaultLanguage);

/// <summary>Read-only lookup of tenants for other modules (implemented by the Tenancy module).</summary>
public interface ITenantDirectory
{
    Task<TenantSummary?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, TenantSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
