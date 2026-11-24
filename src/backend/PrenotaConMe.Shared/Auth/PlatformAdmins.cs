using Microsoft.Extensions.Options;

namespace PrenotaConMe.Shared.Auth;

/// <summary>"Platform" configuration section. Kept in user-secrets / the server's shared settings (public repo).</summary>
public sealed class PlatformOptions
{
    /// <summary>Verified Google e-mails of the platform super-admins (role P in the backlog).</summary>
    public IList<string> AdminEmails { get; } = [];
}

/// <summary>Platform super-admins: people who run PrenotaConMe itself, not a business.</summary>
public sealed class PlatformAdmins(IOptions<PlatformOptions> options)
{
    public bool IsAdmin(ICurrentUser user) =>
        user is { IsAuthenticated: true, EmailVerified: true, Email: { } email }
        && options.Value.AdminEmails.Any(a => string.Equals(a.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
}
