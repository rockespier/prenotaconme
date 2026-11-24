namespace PrenotaConMe.Shared.Auth;

/// <summary>The authenticated caller (Firebase user) of the current request.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Firebase uid (JWT "sub"); null when anonymous.</summary>
    string? ExternalId { get; }

    string? Email { get; }

    /// <summary>Name from the identity provider (Google profile name).</summary>
    string? DisplayName { get; }

    /// <summary>True when the identity provider verified <see cref="Email"/> (always true for Google sign-in).</summary>
    bool EmailVerified { get; }

    /// <summary>
    /// Local user id. Creates the local user record on first use (just-in-time provisioning).
    /// Throws <see cref="InvalidOperationException"/> when the request is anonymous.
    /// </summary>
    Task<Guid> GetUserIdAsync(CancellationToken cancellationToken = default);
}
