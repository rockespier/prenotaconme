using System.Reflection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Shared.Domain;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Infrastructure.Persistence;

/// <summary>
/// Single DbContext for the modular monolith. Each module contributes its entity
/// configurations through a registered <see cref="ModelAssembly"/>.
/// </summary>
public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ITenantContext tenantContext,
    IEnumerable<ModelAssembly> modelAssemblies) : DbContext(options), IDataProtectionKeyContext
{
    public const string TenantFilter = "Tenant";

    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    // Referenced by the query filter; EF evaluates it per query, so each scope sees its own tenant.
    private Guid? CurrentTenantId => tenantContext.TenantId;

    /// <summary>ASP.NET Data Protection key ring (encrypted secrets such as bot tokens, auth cookies), shared by every instance.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Needed for EXCLUDE constraints (no overlapping appointments per staff member).
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        foreach (var assembly in modelAssemblies.Select(m => m.Assembly).Distinct())
        {
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }

        // Ids are assigned client-side (UUIDv7) when the object is constructed. Telling EF they are never
        // store-generated makes it INSERT new children found in navigations, instead of assuming they exist.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => t.BaseType is null && typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        }

        var tenantOwnedRoots = modelBuilder.Model.GetEntityTypes()
            .Where(t => t.BaseType is null && typeof(ITenantOwned).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType)
            .ToList();

        foreach (var clrType in tenantOwnedRoots)
        {
            ApplyTenantFilterMethod.MakeGenericMethod(clrType).Invoke(this, [modelBuilder]);
        }
    }

    // Fail-closed: with no tenant in scope, tenant-owned queries return nothing.
    // Platform-level code must call IgnoreQueryFilters([TenantFilter]) explicitly.
    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder)
        where T : class, ITenantOwned
    {
        var entity = modelBuilder.Entity<T>();
        entity.HasQueryFilter(TenantFilter, e => e.TenantId == CurrentTenantId);
        entity.HasIndex(e => e.TenantId);
    }
}
