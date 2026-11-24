using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Tenancy.Domain;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Tenancy;

internal sealed class TenantDirectory(AppDbContext db, IMemoryCache cache) : ITenantDirectory
{
    private static readonly TimeSpan SlugCacheDuration = TimeSpan.FromMinutes(5);

    public async Task<TenantSummary?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var normalized = TenantRules.NormalizeSlug(slug);
        var key = CacheKey(normalized);

        if (cache.TryGetValue(key, out TenantSummary? cached))
        {
            return cached;
        }

        var summary = await db.Set<Tenant>()
            .AsNoTracking()
            .Where(t => t.Slug == normalized)
            .Select(t => new TenantSummary(t.Id, t.Slug, t.Name, t.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

        // Only positive hits are cached so a newly registered slug is usable immediately.
        if (summary is not null)
        {
            cache.Set(key, summary, SlugCacheDuration);
        }

        return summary;
    }

    public async Task<IReadOnlyDictionary<Guid, TenantSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, TenantSummary>();
        }

        return await db.Set<Tenant>()
            .AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new TenantSummary(t.Id, t.Slug, t.Name, t.IsActive))
            .ToDictionaryAsync(t => t.Id, cancellationToken);
    }

    internal static string CacheKey(string normalizedSlug) => $"tenant-slug:{normalizedSlug}";
}
