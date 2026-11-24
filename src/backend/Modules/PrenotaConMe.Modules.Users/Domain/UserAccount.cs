using PrenotaConMe.Shared.Domain;

namespace PrenotaConMe.Modules.Users.Domain;

/// <summary>Local profile of a Firebase user. Platform-wide (a person can belong to many businesses).</summary>
public sealed class UserAccount : Entity
{
    private UserAccount()
    {
    }

    /// <summary>Firebase uid (JWT "sub").</summary>
    public string ExternalId { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    public string? DisplayName { get; private set; }

    public string? PhotoUrl { get; private set; }

    /// <summary>Asked on first booking (F1-10); used to match WhatsApp/Telegram contacts.</summary>
    public string? PhoneNumber { get; private set; }

    public static UserAccount Create(string externalId, string? email, string? displayName, string? photoUrl) => new()
    {
        ExternalId = externalId,
        Email = email,
        DisplayName = displayName,
        PhotoUrl = photoUrl,
    };

    public void SetPhoneNumber(string e164) => PhoneNumber = e164;

    /// <summary>Keeps the profile in sync with the identity provider. Returns true when something changed.</summary>
    public bool SyncProfile(string? email, string? displayName, string? photoUrl)
    {
        var changed = Email != email || DisplayName != displayName || PhotoUrl != photoUrl;
        Email = email;
        DisplayName = displayName;
        PhotoUrl = photoUrl;
        return changed;
    }
}
