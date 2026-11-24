using System.Net;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Options;

namespace PrenotaConMe.Modules.Notifications.Push;

/// <summary>"Push" configuration section (VAPID keys, base64url). The private key is a secret.</summary>
public sealed class PushOptions
{
    public string? PublicKey { get; set; }

    public string? PrivateKey { get; set; }

    /// <summary>Contact for the push services: "mailto:..." or an https URL.</summary>
    public string? Subject { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}

public sealed record PushTarget(string Endpoint, string P256dh, string Auth);

public enum PushResult
{
    Sent,

    /// <summary>The browser unsubscribed or the subscription expired: delete it.</summary>
    Gone,
}

public interface IPushSender
{
    Task<PushResult> SendAsync(PushTarget target, string payload, CancellationToken cancellationToken);
}

internal sealed class VapidPushSender(HttpClient http, IOptions<PushOptions> options) : IPushSender
{
    public async Task<PushResult> SendAsync(PushTarget target, string payload, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var client = new PushServiceClient(http)
        {
            DefaultAuthentication = new VapidAuthentication(o.PublicKey!, o.PrivateKey!) { Subject = o.Subject },
        };

        var subscription = new PushSubscription { Endpoint = target.Endpoint };
        subscription.SetKey(PushEncryptionKeyName.P256DH, target.P256dh);
        subscription.SetKey(PushEncryptionKeyName.Auth, target.Auth);

        try
        {
            await client.RequestPushMessageDeliveryAsync(
                subscription,
                new PushMessage(payload) { TimeToLive = (int)TimeSpan.FromDays(1).TotalSeconds, Urgency = PushMessageUrgency.High },
                cancellationToken);
            return PushResult.Sent;
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushResult.Gone;
        }
    }
}
