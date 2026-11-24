using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Users.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Users;

/// <summary>Succeeds when the current user holds one of the required roles in the resolved tenant.</summary>
internal sealed class TenantRoleHandler(ICurrentUser currentUser, ITenantContext tenantContext, AppDbContext db)
    : AuthorizationHandler<TenantRoleRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantRoleRequirement requirement)
    {
        if (tenantContext.TenantId is null || !currentUser.IsAuthenticated)
        {
            return;
        }

        var externalId = currentUser.ExternalId;
        var roles = requirement.Roles.ToArray();

        // The tenant query filter already restricts memberships to the current tenant.
        var hasRole = await db.Set<UserMembership>()
            .Where(m => roles.Contains(m.Role))
            .Join(db.Set<UserAccount>(), m => m.UserId, u => u.Id, (m, u) => u.ExternalId)
            .AnyAsync(id => id == externalId);

        if (hasRole)
        {
            context.Succeed(requirement);
        }
    }
}
