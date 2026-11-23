namespace PrenotaConMe.Shared.Tenancy;

/// <summary>Current tenant for the request/job scope. Null means no tenant (platform-level operation).</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }

    void SetTenant(Guid tenantId);
}
