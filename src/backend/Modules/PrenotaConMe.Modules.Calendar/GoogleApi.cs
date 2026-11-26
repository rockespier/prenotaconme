using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrenotaConMe.Shared.Calendar;

namespace PrenotaConMe.Modules.Calendar;

/// <summary>"Google" configuration section: an OAuth web client with the Calendar API enabled.</summary>
public sealed class GoogleOptions
{
    public string? ClientId { get; set; }

    /// <summary>Belongs in user-secrets / the server's shared settings.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Without a client the feature is hidden everywhere.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>Google no longer accepts the refresh token (revoked by the user, or expired): connect again.</summary>
public sealed class GoogleAuthRevokedException(string message) : Exception(message);

/// <summary>Tokens from the OAuth code exchange.</summary>
public sealed record GoogleGrant(string RefreshToken, string AccessToken, TimeSpan ExpiresIn, string? Email);

public sealed record GoogleAccessToken(string Token, TimeSpan ExpiresIn);

/// <summary>An appointment as a Google Calendar event (times are UTC instants).</summary>
public sealed record GoogleEvent(
    string Summary, string Description, string? Location, DateTimeOffset Start, DateTimeOffset End, Guid AppointmentId);

/// <summary>Google OAuth + Calendar v3 REST calls used by the module (plain HttpClient; a fake in tests).</summary>
public interface IGoogleCalendarApi
{
    Task<GoogleGrant> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken);

    /// <exception cref="GoogleAuthRevokedException">The refresh token is no longer valid.</exception>
    Task<GoogleAccessToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);

    /// <returns>The new event id.</returns>
    Task<string> InsertEventAsync(string accessToken, string calendarId, GoogleEvent calendarEvent, CancellationToken cancellationToken);

    /// <returns>False when the event no longer exists (deleted by the user in Google).</returns>
    Task<bool> UpdateEventAsync(string accessToken, string calendarId, string eventId, GoogleEvent calendarEvent, CancellationToken cancellationToken);

    /// <summary>Already deleted events count as deleted.</summary>
    Task DeleteEventAsync(string accessToken, string calendarId, string eventId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BusyInterval>> FreeBusyAsync(
        string accessToken, string calendarId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

internal sealed class HttpGoogleCalendarApi(HttpClient http, GoogleOptions options) : IGoogleCalendarApi
{
    public const string AuthorizeUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    private const string RevokeUrl = "https://oauth2.googleapis.com/revoke";
    private const string CalendarApi = "https://www.googleapis.com/calendar/v3";

    /// <summary>Events in the staff member's calendars, free/busy of the calendars, and their e-mail.</summary>
    public const string Scopes =
        "openid email https://www.googleapis.com/auth/calendar.events https://www.googleapis.com/auth/calendar.freebusy";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<GoogleGrant> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken)
    {
        var token = await PostTokenAsync(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = options.ClientId!,
            ["client_secret"] = options.ClientSecret!,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        }, cancellationToken);
        if (string.IsNullOrEmpty(token.RefreshToken))
        {
            throw new InvalidOperationException("Google returned no refresh token (offline access not granted).");
        }

        return new GoogleGrant(token.RefreshToken, token.AccessToken, TimeSpan.FromSeconds(token.ExpiresIn), EmailOf(token.IdToken));
    }

    public async Task<GoogleAccessToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var token = await PostTokenAsync(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = options.ClientId!,
            ["client_secret"] = options.ClientSecret!,
            ["grant_type"] = "refresh_token",
        }, cancellationToken);
        return new GoogleAccessToken(token.AccessToken, TimeSpan.FromSeconds(token.ExpiresIn));
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        // In the body, not the query string: URLs end up in logs.
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refreshToken });
        using var response = await http.PostAsync(new Uri(RevokeUrl), content, cancellationToken);
    }

    public async Task<string> InsertEventAsync(
        string accessToken, string calendarId, GoogleEvent calendarEvent, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, $"{CalendarApi}/calendars/{Uri.EscapeDataString(calendarId)}/events", accessToken);
        request.Content = JsonContent.Create(Body(calendarEvent), options: Json);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var created = await response.Content.ReadFromJsonAsync<EventResponse>(Json, cancellationToken);
        return created!.Id;
    }

    public async Task<bool> UpdateEventAsync(
        string accessToken, string calendarId, string eventId, GoogleEvent calendarEvent, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Put, EventUrl(calendarId, eventId), accessToken);
        request.Content = JsonContent.Create(Body(calendarEvent), options: Json);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return false;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    public async Task DeleteEventAsync(string accessToken, string calendarId, string eventId, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Delete, EventUrl(calendarId, eventId), accessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Gone))
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<BusyInterval>> FreeBusyAsync(
        string accessToken, string calendarId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, $"{CalendarApi}/freeBusy", accessToken);
        request.Content = JsonContent.Create(
            new { timeMin = from.UtcDateTime, timeMax = to.UtcDateTime, items = new[] { new { id = calendarId } } }, options: Json);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<FreeBusyResponse>(Json, cancellationToken);
        if (result?.Calendars is null || !result.Calendars.TryGetValue(calendarId, out var calendar))
        {
            return [];
        }

        if (calendar.Errors is { Count: > 0 })
        {
            throw new HttpRequestException($"FreeBusy error: {calendar.Errors[0].Reason}");
        }

        return calendar.Busy?.Select(b => new BusyInterval(b.Start, b.End)).ToList() ?? [];
    }

    private async Task<TokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(new Uri(TokenUrl), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<TokenError>(Json, cancellationToken);
            if (error?.Error == "invalid_grant")
            {
                throw new GoogleAuthRevokedException(error.ErrorDescription ?? "invalid_grant");
            }

            throw new HttpRequestException($"Google token endpoint returned {(int)response.StatusCode} ({error?.Error}).");
        }

        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Json, cancellationToken))!;
    }

    private static string EventUrl(string calendarId, string eventId) =>
        $"{CalendarApi}/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";

    private static HttpRequestMessage Request(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, new Uri(url));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new GoogleAuthRevokedException("Access token rejected.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Google Calendar returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 300)]}");
        }
    }

    /// <summary>
    /// Transparent: the event shows in the staff member's calendar but does not make them busy, so FreeBusy (F2-09)
    /// never counts an appointment twice or blocks moving it.
    /// </summary>
    private static object Body(GoogleEvent e) => new
    {
        summary = e.Summary,
        description = e.Description,
        location = e.Location,
        start = new { dateTime = e.Start.UtcDateTime },
        end = new { dateTime = e.End.UtcDateTime },
        transparency = "transparent",
        extendedProperties = new { @private = new Dictionary<string, string> { ["prenotaconmeAppointmentId"] = e.AppointmentId.ToString() } },
    };

    /// <summary>
    /// The e-mail claim of the ID token. It comes straight from Google's token endpoint over TLS, so it is read
    /// without checking the signature (OpenID Connect allows this for the code flow).
    /// </summary>
    internal static string? EmailOf(string? idToken)
    {
        var parts = idToken?.Split('.');
        if (parts is not { Length: 3 })
        {
            return null;
        }

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return document.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("id_token")] string? IdToken);

    private sealed record TokenError(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);

    private sealed record EventResponse(string Id);

    private sealed record FreeBusyResponse(Dictionary<string, FreeBusyCalendar>? Calendars);

    private sealed record FreeBusyCalendar(List<FreeBusySlot>? Busy, List<FreeBusyError>? Errors);

    private sealed record FreeBusySlot(DateTimeOffset Start, DateTimeOffset End);

    private sealed record FreeBusyError(string? Reason);
}
