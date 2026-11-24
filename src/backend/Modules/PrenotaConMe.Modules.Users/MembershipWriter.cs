using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Users.Domain;
using PrenotaConMe.Shared.Auth;

namespace PrenotaConMe.Modules.Users;

internal sealed class MembershipWriter(AppDbContext db) : IMembershipWriter
{
    public async Task EnsureAsync(Guid tenantId, Guid userId, TenantRole role, CancellationToken cancellationToken = default)
    {
        var staged = db.Set<UserMembership>().Local
            .Any(m => m.TenantId == tenantId && m.UserId == userId && m.Role == role);

        if (staged || await Query(tenantId, userId, role).AnyAsync(cancellationToken))
        {
            return;
        }

        db.Add(UserMembership.Create(tenantId, userId, role));
    }

    public async Task RemoveAsync(Guid tenantId, Guid userId, TenantRole role, CancellationToken cancellationToken = default)
    {
        var existing = await Query(tenantId, userId, role).ToListAsync(cancellationToken);
        db.RemoveRange(existing);
    }

    // Cross-tenant by design: callers pass the tenant explicitly.
    private IQueryable<UserMembership> Query(Guid tenantId, Guid userId, TenantRole role) =>
        db.Set<UserMembership>()
            .IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(m => m.TenantId == tenantId && m.UserId == userId && m.Role == role);
}
