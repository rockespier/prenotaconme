using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Shared.Auth;

/// <summary>Caller must hold at least one of <see cref="Roles"/> in the current tenant.</summary>
public sealed class TenantRoleRequirement(IReadOnlyCollection<TenantRole> roles) : IAuthorizationRequirement
{
    public IReadOnlyCollection<TenantRole> Roles { get; } = roles;
}

public static class TenantRoleEndpointExtensions
{
    /// <summary>Requires an authenticated user with one of <paramref name="roles"/> in the tenant resolved for this request.</summary>
    public static TBuilder RequireTenantRole<TBuilder>(this TBuilder builder, params TenantRole[] roles)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentOutOfRangeException.ThrowIfZero(roles.Length);

        return builder
            .RequireTenant()
            .RequireAuthorization(policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantRoleRequirement(roles)));
    }
}
