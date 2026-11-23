namespace PrenotaConMe.Shared.Domain;

/// <summary>Base entity with a time-ordered (UUIDv7) id and audit timestamps.</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
