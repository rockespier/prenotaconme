using PrenotaConMe.Shared.Domain;

namespace PrenotaConMe.Modules.Notifications.Domain;

/// <summary>
/// F2-02: one browser/device that accepted push notifications. Platform-wide (not tenant-owned): the same
/// device receives messages from every business the user deals with.
/// </summary>
public sealed class WebPushSubscription : Entity
{
    public const int EndpointMaxLength = 1024;
    public const int KeyMaxLength = 256;

    private WebPushSubscription()
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>Push service URL; unique per browser installation.</summary>
    public string Endpoint { get; private set; } = string.Empty;

    public string P256dh { get; private set; } = string.Empty;

    public string Auth { get; private set; } = string.Empty;

    public static WebPushSubscription Create(Guid userId, string endpoint, string p256dh, string auth) =>
        new() { UserId = userId, Endpoint = endpoint, P256dh = p256dh, Auth = auth };

    /// <summary>The same browser re-subscribed (new keys) or another account signed in on it.</summary>
    public void Update(Guid userId, string p256dh, string auth)
    {
        UserId = userId;
        P256dh = p256dh;
        Auth = auth;
    }
}
