using System.Collections.Concurrent;
using PrenotaConMe.Modules.Calendar;
using PrenotaConMe.Shared.Calendar;

namespace PrenotaConMe.IntegrationTests;

/// <summary>
/// Replaces Google in tests. The OAuth code becomes the refresh token "refresh-{code}" and the access token
/// "access-{code}"; events live in <see cref="Events"/>; <see cref="Busy"/> scripts FreeBusy per access token.
/// </summary>
public sealed class FakeGoogleCalendarApi : IGoogleCalendarApi
{
    public ConcurrentDictionary<string, GoogleEvent> Events { get; } = new();

    public ConcurrentDictionary<string, IReadOnlyList<BusyInterval>> Busy { get; } = new();

    public ConcurrentBag<string> Revoked { get; } = [];

    public static string AccessToken(string code) => "access-" + code;

    public Task<GoogleGrant> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken) =>
        Task.FromResult(new GoogleGrant("refresh-" + code, AccessToken(code), TimeSpan.FromHours(1), $"{code}@gmail.com"));

    public Task<GoogleAccessToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        Revoked.Contains(refreshToken)
            ? throw new GoogleAuthRevokedException("revoked")
            : Task.FromResult(new GoogleAccessToken(AccessToken(refreshToken["refresh-".Length..]), TimeSpan.FromHours(1)));

    public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        Revoked.Add(refreshToken);
        return Task.CompletedTask;
    }

    public Task<string> InsertEventAsync(string accessToken, string calendarId, GoogleEvent calendarEvent, CancellationToken cancellationToken)
    {
        var id = $"ev{Guid.NewGuid():N}";
        Events[id] = calendarEvent;
        return Task.FromResult(id);
    }

    public Task<bool> UpdateEventAsync(
        string accessToken, string calendarId, string eventId, GoogleEvent calendarEvent, CancellationToken cancellationToken)
    {
        if (!Events.ContainsKey(eventId))
        {
            return Task.FromResult(false);
        }

        Events[eventId] = calendarEvent;
        return Task.FromResult(true);
    }

    public Task DeleteEventAsync(string accessToken, string calendarId, string eventId, CancellationToken cancellationToken)
    {
        Events.TryRemove(eventId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BusyInterval>> FreeBusyAsync(
        string accessToken, string calendarId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        Task.FromResult(Busy.GetValueOrDefault(accessToken) ?? []);

    /// <summary>Waits for the export job (Hangfire) to reach the expected state.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 300; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Google Calendar fake never reached the expected state.");
    }
}
