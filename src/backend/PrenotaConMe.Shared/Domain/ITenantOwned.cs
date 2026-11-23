namespace PrenotaConMe.Shared.Domain;

/// <summary>Marks an entity that belongs to a single tenant (business). A global query filter isolates it.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
