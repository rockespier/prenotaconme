using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PrenotaConMe.Shared.Domain;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Infrastructure.Persistence;

/// <summary>Sets audit timestamps and stamps/guards TenantId on save.</summary>
public sealed class AuditingInterceptor(ITenantContext tenantContext, TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is Entity entity)
            {
                if (entry.State == EntityState.Added)
                {
                    entity.CreatedAt = now;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entity.UpdatedAt = now;
                }
            }

            if (entry.Entity is ITenantOwned owned)
            {
                GuardTenant(entry.State, owned, entry.Property(nameof(ITenantOwned.TenantId)).IsModified);
            }
        }
    }

    private void GuardTenant(EntityState state, ITenantOwned owned, bool tenantIdModified)
    {
        var current = tenantContext.TenantId;

        if (state == EntityState.Added)
        {
            if (owned.TenantId == Guid.Empty)
            {
                owned.TenantId = current ?? throw new MissingTenantException();
            }
            else if (current is { } tenant && owned.TenantId != tenant)
            {
                throw new InvalidOperationException("Cannot create an entity for a different tenant.");
            }
        }
        else if (state == EntityState.Modified && tenantIdModified)
        {
            throw new InvalidOperationException("TenantId cannot be changed.");
        }
    }
}
