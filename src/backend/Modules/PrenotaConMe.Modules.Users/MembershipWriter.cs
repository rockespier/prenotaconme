using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Users.Domain;
using PrenotaConMe.Shared.Auth;

namespace PrenotaConMe.Modules.Users;

internal sealed class MembershipWriter(AppDbContext db) : IMembershipWriter
{
    public void Add(Guid tenantId, Guid userId, TenantRole role) =>
        db.Add(UserMembership.Create(tenantId, userId, role));
}
