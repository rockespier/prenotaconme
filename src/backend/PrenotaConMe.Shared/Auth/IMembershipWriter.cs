namespace PrenotaConMe.Shared.Auth;

/// <summary>
/// Lets other modules grant tenant roles. Only stages the change on the shared DbContext:
/// the caller's SaveChanges commits it atomically with its own changes.
/// </summary>
public interface IMembershipWriter
{
    void Add(Guid tenantId, Guid userId, TenantRole role);
}
