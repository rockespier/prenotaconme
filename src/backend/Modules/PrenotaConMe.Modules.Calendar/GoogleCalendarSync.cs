using System.Globalization;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Calendar.Domain;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Calendar;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Calendar;

/// <summary>
/// Access tokens of a connection, cached until shortly before they expire. A refresh token Google no longer
/// accepts marks the connection as failed (<see cref="GoogleCalendarConnection.Error"/>) with a direct UPDATE, so
/// it never saves other pending changes of the request.
/// </summary>
public sealed partial class GoogleTokens(
    AppDbContext db, IGoogleCalendarApi api, IDataProtectionProvider protection, IMemoryCache cache, ILogger<GoogleTokens> logger)
{
    public const string RefreshTokenPurpose = "Calendar.GoogleRefreshToken";
    public const string Revoked = "revoked";

    public string Protect(string refreshToken) => protection.CreateProtector(RefreshTokenPurpose).Protect(refreshToken);

    public string Unprotect(GoogleCalendarConnection connection) =>
        protection.CreateProtector(RefreshTokenPurpose).Unprotect(connection.ProtectedRefreshToken);

    /// <returns>Null when the connection no longer works.</returns>
    public async Task<string?> GetAsync(GoogleCalendarConnection connection, CancellationToken ct)
    {
        if (connection.Error is not null)
        {
            return null;
        }

        var key = $"gcal-token:{connection.Id}:{connection.ProtectedRefreshToken.GetHashCode(StringComparison.Ordinal)}";
        if (cache.TryGetValue(key, out string? cached))
        {
            return cached;
        }

        try
        {
            var token = await api.RefreshAsync(Unprotect(connection), ct);
            cache.Set(key, token.Token, token.ExpiresIn - TimeSpan.FromMinutes(2));
            return token.Token;
        }
        catch (GoogleAuthRevokedException)
        {
            await db.Set<GoogleCalendarConnection>()
                .Where(c => c.Id == connection.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Error, Revoked), ct);
            LogRevoked(logger, connection.StaffMemberId);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Google Calendar access of staff member {StaffMemberId} was revoked")]
    private static partial void LogRevoked(ILogger logger, Guid staffMemberId);
}

/// <summary>
/// F2-09: busy times from the staff members' Google calendars (FreeBusy), cached for <see cref="CacheFor"/>. Only
/// when the business turned the sync on. Any failure counts as "nothing busy" (logged): Google being down must
/// not stop bookings.
/// </summary>
internal sealed partial class GoogleBusyTimes(
    AppDbContext db, IGoogleCalendarApi api, GoogleTokens tokens, IOptions<GoogleOptions> options, ITenantContext tenant,
    ITenantDirectory tenants, IMemoryCache cache, ILogger<GoogleBusyTimes> logger) : IExternalBusyTimes
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(90);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<BusyInterval>>> GetAsync(
        IReadOnlyCollection<Guid> staffMemberIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, IReadOnlyList<BusyInterval>>();
        if (!options.Value.IsConfigured || staffMemberIds.Count == 0 || tenant.TenantId is not { } tenantId
            || !(await tenants.GetByIdsAsync([tenantId], cancellationToken)).TryGetValue(tenantId, out var business)
            || !business.GoogleCalendarSync)
        {
            return result;
        }

        var connections = await db.Set<GoogleCalendarConnection>().AsNoTracking()
            .Where(c => staffMemberIds.Contains(c.StaffMemberId) && c.Error == null)
            .ToListAsync(cancellationToken);

        // One at a time: the token lookup may write through the request's DbContext.
        foreach (var connection in connections)
        {
            var key = $"gcal-busy:{connection.Id}:{from.UtcTicks}:{to.UtcTicks}";
            if (!cache.TryGetValue(key, out IReadOnlyList<BusyInterval>? busy))
            {
                busy = await ReadAsync(connection, from, to, cancellationToken);
                if (busy is not null)
                {
                    cache.Set(key, busy, CacheFor);
                }
            }

            if (busy is { Count: > 0 })
            {
                result[connection.StaffMemberId] = busy;
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<BusyInterval>?> ReadAsync(
        GoogleCalendarConnection connection, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        try
        {
            var token = await tokens.GetAsync(connection, ct);
            return token is null ? null : await api.FreeBusyAsync(token, connection.CalendarId, from, to, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, connection.StaffMemberId);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Google FreeBusy failed for staff member {StaffMemberId}; free times ignore their calendar")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid staffMemberId);
}

/// <summary>F2-08: queues <see cref="CalendarExportJob"/> when Google is configured.</summary>
internal sealed partial class GoogleAppointmentSync(
    IBackgroundJobClient jobs, IOptions<GoogleOptions> options, ILogger<GoogleAppointmentSync> logger) : IAppointmentCalendarSync
{
    public void Changed(Guid appointmentId)
    {
        if (!options.Value.IsConfigured)
        {
            return;
        }

        try
        {
            jobs.Enqueue<CalendarExportJob>(j => j.SyncAsync(appointmentId, CancellationToken.None));
        }
        catch (Exception ex)
        {
            LogEnqueueFailed(logger, ex, appointmentId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not queue the Google Calendar sync of appointment {AppointmentId}")]
    private static partial void LogEnqueueFailed(ILogger logger, Exception exception, Guid appointmentId);
}

/// <summary>
/// F2-08: puts an appointment in the Google calendar of its staff member as it is now: created, updated, moved to
/// the new staff member's calendar or deleted when cancelled. Idempotent (it reads the current state), so retries
/// and out-of-order runs end in the same place; runs one at a time so two quick changes never create two events.
/// </summary>
public sealed class CalendarExportJob(
    AppDbContext db, IGoogleCalendarApi api, GoogleTokens tokens, ITenantContext tenant, ITenantDirectory tenants, IUserDirectory users)
{
    [AutomaticRetry(Attempts = 5)]
    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task SyncAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var business = (await tenants.GetByIdsAsync([tenant.TenantId!.Value], cancellationToken))[tenant.TenantId!.Value];
        var appointment = await db.Set<Appointment>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);
        var link = await db.Set<CalendarEventLink>().SingleOrDefaultAsync(l => l.AppointmentId == appointmentId, cancellationToken);
        Guid? target = business.GoogleCalendarSync && appointment is { Status: not AppointmentStatus.Cancelled } ? appointment.StaffMemberId : null;

        if (link is not null && link.StaffMemberId != target)
        {
            if (await ConnectionAsync(link.StaffMemberId, cancellationToken) is { } previous
                && await tokens.GetAsync(previous, cancellationToken) is { } previousToken)
            {
                await api.DeleteEventAsync(previousToken, previous.CalendarId, link.EventId, cancellationToken);
            }

            db.Remove(link);
            await db.SaveChangesAsync(cancellationToken);
            link = null;
        }

        if (target is not { } staffId || appointment is null
            || await ConnectionAsync(staffId, cancellationToken) is not { } connection
            || await tokens.GetAsync(connection, cancellationToken) is not { } token)
        {
            return;
        }

        var calendarEvent = await BuildEventAsync(appointment, business.DefaultLanguage, cancellationToken);
        if (link is null)
        {
            var eventId = await api.InsertEventAsync(token, connection.CalendarId, calendarEvent, cancellationToken);
            db.Add(CalendarEventLink.Create(appointment.Id, staffId, eventId));
        }
        else if (!await api.UpdateEventAsync(token, connection.CalendarId, link.EventId, calendarEvent, cancellationToken))
        {
            // Deleted in Google by the staff member: put it back, the appointment still exists.
            link.Replace(await api.InsertEventAsync(token, connection.CalendarId, calendarEvent, cancellationToken));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private Task<GoogleCalendarConnection?> ConnectionAsync(Guid staffId, CancellationToken ct) =>
        db.Set<GoogleCalendarConnection>().AsNoTracking().SingleOrDefaultAsync(c => c.StaffMemberId == staffId, ct);

    private async Task<GoogleEvent> BuildEventAsync(Appointment appointment, string language, CancellationToken ct)
    {
        var location = await db.Set<Location>().AsNoTracking().SingleAsync(l => l.Id == appointment.LocationId, ct);
        var client = appointment.ClientUserId is { } clientId
            ? (await users.GetByIdsAsync([clientId], ct)).GetValueOrDefault(clientId)
            : null;
        var name = appointment.ClientName ?? client?.DisplayName;
        var phone = appointment.ClientPhone ?? client?.PhoneNumber;
        var t = EventTexts.For(language);

        var description = string.Join("\n", new[]
        {
            name is null ? null : $"{t.Client}: {name}",
            phone is null ? null : $"{t.Phone}: {phone}",
            string.Format(CultureInfo.InvariantCulture, t.Price, appointment.Price),
            t.Footer,
        }.Where(l => l is not null));
        var place = string.Join(", ", new[] { location.Name, location.Address, location.City }.Where(p => !string.IsNullOrWhiteSpace(p)));

        return new GoogleEvent(
            name is null ? appointment.ServiceName : $"{appointment.ServiceName} · {name}",
            description, place, appointment.StartsAt, appointment.EndsAt, appointment.Id);
    }

    private sealed record EventTexts(string Client, string Phone, string Price, string Footer)
    {
        public static EventTexts For(string language) => language switch
        {
            "es" => new("Cliente", "Teléfono", "Precio: {0:0.00} €", "Reservado con PrenotaConMe"),
            "en" => new("Client", "Phone", "Price: €{0:0.00}", "Booked with PrenotaConMe"),
            _ => new("Cliente", "Telefono", "Prezzo: {0:0.00} €", "Prenotato con PrenotaConMe"),
        };
    }
}
