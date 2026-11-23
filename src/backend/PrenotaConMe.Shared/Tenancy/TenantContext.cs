namespace PrenotaConMe.Shared.Tenancy;

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }

    public void SetTenant(Guid tenantId)
    {
        if (TenantId is { } current && current != tenantId)
        {
            throw new InvalidOperationException("Tenant already set for this scope.");
        }

        TenantId = tenantId;
    }
}
