namespace PrenotaConMe.Shared.Auth;

/// <summary>Contact details of a local user, for the backoffice (client of an appointment).</summary>
public sealed record UserSummary(Guid Id, string? DisplayName, string? Email, string? PhoneNumber);

/// <summary>Read-only lookup of local users for other modules (implemented by the Users module).</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, UserSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
