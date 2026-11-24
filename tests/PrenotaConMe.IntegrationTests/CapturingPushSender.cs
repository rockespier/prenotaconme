using System.Collections.Concurrent;
using System.Text.Json;
using PrenotaConMe.Modules.Notifications.Push;

namespace PrenotaConMe.IntegrationTests;

/// <summary>Replaces the push services in tests. Endpoints containing "gone" answer like an expired subscription.</summary>
public sealed class CapturingPushSender : IPushSender
{
    private readonly ConcurrentQueue<SentPush> _sent = new();

    public sealed record SentPush(string Endpoint, string Title, string Body, string Url);

    public Task<PushResult> SendAsync(PushTarget target, string payload, CancellationToken cancellationToken)
    {
        if (target.Endpoint.Contains("gone", StringComparison.Ordinal))
        {
            return Task.FromResult(PushResult.Gone);
        }

        using var json = JsonDocument.Parse(payload);
        var n = json.RootElement.GetProperty("notification");
        _sent.Enqueue(new SentPush(
            target.Endpoint, n.GetProperty("title").GetString()!, n.GetProperty("body").GetString()!,
            n.GetProperty("data").GetProperty("onActionClick").GetProperty("default").GetProperty("url").GetString()!));
        return Task.FromResult(PushResult.Sent);
    }

    /// <summary>Waits (sending happens in background jobs) until <paramref name="count"/> pushes reached the endpoint.</summary>
    public async Task<IReadOnlyList<SentPush>> WaitForAsync(string endpoint, int count)
    {
        for (var i = 0; i < 100; i++)
        {
            var found = For(endpoint);
            if (found.Count >= count)
            {
                return found;
            }

            await Task.Delay(50);
        }

        return For(endpoint);
    }

    public IReadOnlyList<SentPush> For(string endpoint) => _sent.Where(p => p.Endpoint == endpoint).ToList();
}
