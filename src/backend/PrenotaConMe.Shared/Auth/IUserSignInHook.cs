namespace PrenotaConMe.Shared.Auth;

/// <summary>
/// Runs when a user loads their profile after signing in (GET /api/users/me), e.g. to link staff invitations
/// to the account by e-mail. Hooks stage changes; the caller saves them.
/// </summary>
public interface IUserSignInHook
{
    /// <param name="verifiedEmail">Lowercased e-mail, only when the identity provider verified it; otherwise null.</param>
    Task OnSignedInAsync(Guid userId, string? verifiedEmail, CancellationToken cancellationToken);
}
