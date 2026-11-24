namespace PrenotaConMe.Shared.Tenancy;

public sealed record TenantSummary(Guid Id, string Slug, string Name, bool IsActive);

/// <summary>Read-only lookup of tenants for other modules (implemented by the Tenancy module).</summary>
public interface ITenantDirectory
{
    Task<TenantSummary?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, TenantSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
