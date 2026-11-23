using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.UnitTests.Tenancy;

public sealed class TenantContextTests
{
    [Fact]
    public void SetTenant_same_tenant_twice_is_allowed()
    {
        var ctx = new TenantContext();
        var id = Guid.CreateVersion7();

        ctx.SetTenant(id);
        ctx.SetTenant(id);

        Assert.Equal(id, ctx.TenantId);
    }

    [Fact]
    public void SetTenant_different_tenant_throws()
    {
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.CreateVersion7());

        Assert.Throws<InvalidOperationException>(() => ctx.SetTenant(Guid.CreateVersion7()));
    }
}
