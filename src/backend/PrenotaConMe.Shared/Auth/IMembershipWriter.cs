namespace PrenotaConMe.Shared.Auth;

/// <summary>
/// Lets other modules grant or revoke tenant roles. Changes are only staged on the shared DbContext:
/// the caller's SaveChanges commits them atomically with its own changes. Works across tenants
/// (e.g. linking a staff invitation while no tenant is resolved).
/// </summary>
public interface IMembershipWriter
{
    /// <summary>Grants the role unless the user already has it in that tenant.</summary>
    Task EnsureAsync(Guid tenantId, Guid userId, TenantRole role, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid tenantId, Guid userId, TenantRole role, CancellationToken cancellationToken = default);
}
