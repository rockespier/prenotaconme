namespace PrenotaConMe.Shared.Domain;

public abstract class TenantEntity : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
}
