using System.Globalization;
using System.Security.Cryptography;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Calendar.Domain;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Tenancy;
using PrenotaConMe.Shared.Hosting;

namespace PrenotaConMe.Modules.Calendar;

/// <param name="Configured">The platform has a Google OAuth client (otherwise the feature is hidden).</param>
/// <param name="EnabledByBusiness">The Admin turned the sync on in the business settings.</param>
/// <param name="HasStaffProfile">The current user is a staff member of this business (only they can connect).</param>
/// <param name="Error">"revoked": Google no longer accepts the connection; connect again.</param>
public sealed record GoogleCalendarStatusDto(
    bool Configured, bool EnabledByBusiness, bool HasStaffProfile, bool Connected, string? GoogleEmail, string? Error);

public sealed record GoogleConnectDto(string Url);

/// <summary>
/// F2-07: a staff member connects their own Google Calendar (OAuth code flow with offline access) and can
/// disconnect it. The OAuth state is a time-limited Data Protection payload that carries tenant, staff member and
/// user, so the anonymous callback knows where to store the connection.
/// </summary>
internal static partial class GoogleCalendarEndpoints
{
    public const string CallbackPath = "/api/calendar/google/callback";
    private const string StatePurpose = "Calendar.GoogleOAuthState";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/google/status", StatusAsync)
            .RequireTenantRole(TenantRole.Admin, TenantRole.Staff)
            .WithName("GetGoogleCalendarStatus");

        group.MapPost("/google/connect", ConnectAsync)
            .RequireTenantRole(TenantRole.Admin, TenantRole.Staff)
            .WithName("ConnectGoogleCalendar")
            .RequireRateLimiting(RateLimits.Sensitive);

        group.MapGet("/google/callback", CallbackAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

        group.MapDelete("/google/connection", DisconnectAsync)
            .RequireTenantRole(TenantRole.Admin, TenantRole.Staff)
            .WithName("DisconnectGoogleCalendar");
    }

    private static async Task<Ok<GoogleCalendarStatusDto>> StatusAsync(
        AppDbContext db, ICurrentUser user, ITenantContext tenant, ITenantDirectory tenants, IOptions<GoogleOptions> options,
        CancellationToken ct)
    {
        var business = (await tenants.GetByIdsAsync([tenant.TenantId!.Value], ct))[tenant.TenantId!.Value];
        var staff = await StaffOfAsync(db, user, ct);
        var connection = staff is null
            ? null
            : await db.Set<GoogleCalendarConnection>().AsNoTracking().SingleOrDefaultAsync(c => c.StaffMemberId == staff.Id, ct);
        return TypedResults.Ok(new GoogleCalendarStatusDto(
            options.Value.IsConfigured, business.GoogleCalendarSync, staff is not null, connection is not null,
            connection?.GoogleEmail, connection?.Error));
    }

    private static async Task<Results<Ok<GoogleConnectDto>, ProblemHttpResult>> ConnectAsync(
        HttpContext http, AppDbContext db, ICurrentUser user, ITenantContext tenant, ITenantDirectory tenants,
        IOptions<GoogleOptions> options, IDataProtectionProvider protection, IConfiguration configuration, CancellationToken ct)
    {
        var business = (await tenants.GetByIdsAsync([tenant.TenantId!.Value], ct))[tenant.TenantId!.Value];
        if (!options.Value.IsConfigured || !business.GoogleCalendarSync)
        {
            return Conflict("calendar.notEnabled");
        }

        if (await StaffOfAsync(db, user, ct) is not { } staff)
        {
            return Conflict("calendar.noStaffProfile");
        }

        var userId = await user.GetUserIdAsync(ct);
        var state = protection.CreateProtector(StatePurpose).ToTimeLimitedDataProtector()
            .Protect($"{business.Id:N}|{staff.Id:N}|{userId:N}|{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}", StateLifetime);
        var query = new Dictionary<string, string>
        {
            ["client_id"] = options.Value.ClientId!,
            ["redirect_uri"] = SiteUrl(http, configuration) + CallbackPath,
            ["response_type"] = "code",
            ["scope"] = HttpGoogleCalendarApi.Scopes,
            ["access_type"] = "offline",
            // Always asks again, so Google always returns a refresh token (also when reconnecting).
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "true",
            ["state"] = state,
        };
        if (user.Email is { } email)
        {
            query["login_hint"] = email;
        }

        var url = HttpGoogleCalendarApi.AuthorizeUrl + "?" + string.Join("&",
            query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return TypedResults.Ok(new GoogleConnectDto(url));
    }

    /// <summary>Google redirects the browser here; the result goes back to the dashboard as ?google=connected|denied|failed.</summary>
    private static async Task<RedirectHttpResult> CallbackAsync(
        string? code, string? state, string? error, HttpContext http, AppDbContext db, ITenantContext tenant,
        IGoogleCalendarApi api, GoogleTokens tokens, IDataProtectionProvider protection, IConfiguration configuration,
        IBackgroundJobClient jobs, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        var site = SiteUrl(http, configuration);
        RedirectHttpResult Back(string result) => TypedResults.Redirect($"{site}/admin?google={result}");

        Guid tenantId, staffId, userId;
        try
        {
            var parts = protection.CreateProtector(StatePurpose).ToTimeLimitedDataProtector().Unprotect(state ?? string.Empty).Split('|');
            (tenantId, staffId, userId) = (Guid.Parse(parts[0]), Guid.Parse(parts[1]), Guid.Parse(parts[2]));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or IndexOutOfRangeException)
        {
            return Back("failed");
        }

        if (error is not null || string.IsNullOrEmpty(code))
        {
            return Back("denied");
        }

        tenant.SetTenant(tenantId);
        try
        {
            var granted = await api.ExchangeCodeAsync(code, site + CallbackPath, ct);
            var protectedToken = tokens.Protect(granted.RefreshToken);
            var connection = await db.Set<GoogleCalendarConnection>().SingleOrDefaultAsync(c => c.StaffMemberId == staffId, ct);
            if (connection is null)
            {
                db.Add(GoogleCalendarConnection.Create(staffId, userId, granted.Email, protectedToken));
            }
            else
            {
                connection.Reconnect(userId, granted.Email, protectedToken);
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogConnectFailed(loggers.CreateLogger("PrenotaConMe.Modules.Calendar"), ex, staffId);
            return Back("failed");
        }

        // Upcoming appointments go to the calendar right away, not only after their next change.
        var now = clock.GetUtcNow();
        var upcoming = await db.Set<Appointment>().AsNoTracking()
            .Where(a => a.StaffMemberId == staffId && a.Status != AppointmentStatus.Cancelled && a.StartsAt > now)
            .Select(a => a.Id)
            .ToListAsync(ct);
        foreach (var id in upcoming)
        {
            jobs.Enqueue<CalendarExportJob>(j => j.SyncAsync(id, CancellationToken.None));
        }

        return Back("connected");
    }

    private static async Task<NoContent> DisconnectAsync(
        AppDbContext db, ICurrentUser user, IGoogleCalendarApi api, GoogleTokens tokens, CancellationToken ct)
    {
        if (await StaffOfAsync(db, user, ct) is { } staff
            && await db.Set<GoogleCalendarConnection>().SingleOrDefaultAsync(c => c.StaffMemberId == staff.Id, ct) is { } connection)
        {
            try
            {
                await api.RevokeAsync(tokens.Unprotect(connection), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or CryptographicException)
            {
                // Best effort: the connection is deleted here anyway.
            }

            // The events already in Google stay there; later changes no longer touch them.
            await db.Set<CalendarEventLink>().Where(l => l.StaffMemberId == staff.Id).ExecuteDeleteAsync(ct);
            db.Remove(connection);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<StaffMember?> StaffOfAsync(AppDbContext db, ICurrentUser user, CancellationToken ct)
    {
        var userId = await user.GetUserIdAsync(ct);
        return await db.Set<StaffMember>().AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
    }

    /// <summary>The public site (Notifications:PublicUrl, the SPA's origin), or this request's origin.</summary>
    private static string SiteUrl(HttpContext http, IConfiguration configuration) =>
        configuration["Notifications:PublicUrl"]?.TrimEnd('/') is { Length: > 0 } url
            ? url
            : string.Create(CultureInfo.InvariantCulture, $"{http.Request.Scheme}://{http.Request.Host}");

    private static ProblemHttpResult Conflict(string title) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: title);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not connect the Google Calendar of staff member {StaffMemberId}")]
    private static partial void LogConnectFailed(ILogger logger, Exception exception, Guid staffMemberId);
}
