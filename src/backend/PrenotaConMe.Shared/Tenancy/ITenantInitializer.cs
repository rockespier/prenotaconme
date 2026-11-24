namespace PrenotaConMe.Shared.Tenancy;

public sealed record TenantInitializationContext(Guid TenantId, string TimeZone, string Language, Guid OwnerUserId);

/// <summary>
/// Lets a module create starter data when a business registers (default location, sample services, ...).
/// Initializers only stage entities on the shared DbContext; registration commits everything in one SaveChanges.
/// They run in ascending <see cref="Order"/>; a later one may read entities staged by an earlier one via DbSet.Local.
/// </summary>
public interface ITenantInitializer
{
    int Order { get; }

    Task InitializeAsync(TenantInitializationContext context, CancellationToken cancellationToken);
}
