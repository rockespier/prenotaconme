using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Scheduling;

/// <param name="Key">Stable id of the client: the user id, or "guest:" + phone (or name) for clients without an account.</param>
/// <param name="Spent">Sum of the prices of past, non-cancelled appointments.</param>
public sealed record ClientSummaryDto(
    string Key, string? Name, string? Email, string? Phone, bool IsGuest,
    int Visits, int Cancelled, decimal Spent, DateTimeOffset? LastVisit, DateTimeOffset? NextAppointment);

public sealed record ClientAppointmentDto(
    Guid Id, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string ServiceName, decimal Price,
    string StaffName, string LocationName, string TimeZone, AppointmentStatus Status);

public sealed record ClientDetailDto(ClientSummaryDto Client, IReadOnlyList<ClientAppointmentDto> Appointments);

/// <summary>
/// F1-14: the business's clients, derived from its appointments (registered clients by account, guests entered
/// in the calendar by phone, or by name when there is no phone).
/// </summary>
internal static class ClientsEndpoints
{
    public const string GuestPrefix = "guest:";

    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/clients", ListAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("ListClients");

        group.MapGet("/clients/detail", GetAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("GetClient");

        // F5-04: a client asked the business to erase their data (GDPR art. 17).
        group.MapPost("/clients/erase", EraseAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("EraseClient");
    }

    /// <summary>
    /// Anonymizes the client's appointments in this business (future ones are cancelled) and removes their Client
    /// role here. Their account and data in other businesses stay: only they can delete those.
    /// </summary>
    private static async Task<Results<NoContent, NotFound>> EraseAsync(
        string key, AppDbContext db, IMembershipWriter memberships, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        IQueryable<Appointment> query = db.Set<Appointment>();
        Guid? userId = null;
        if (key.StartsWith(GuestPrefix, StringComparison.Ordinal))
        {
            var value = key[GuestPrefix.Length..];
            query = query.Where(a => a.ClientUserId == null
                && (a.ClientPhone == value || (a.ClientPhone == null && a.ClientName == value)));
        }
        else if (Guid.TryParse(key, out var id))
        {
            userId = id;
            query = query.Where(a => a.ClientUserId == id);
        }
        else
        {
            return TypedResults.NotFound();
        }

        var appointments = await query.ToListAsync(ct);
        if (appointments.Count == 0)
        {
            return TypedResults.NotFound();
        }

        var now = clock.GetUtcNow();
        foreach (var appointment in appointments)
        {
            appointment.EraseClient(now);
        }

        if (userId is { } clientId)
        {
            await memberships.RemoveAsync(tenant.TenantId!.Value, clientId, TenantRole.Client, ct);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Key of the client of an appointment (also returned by the calendar).</summary>
    public static string KeyOf(Appointment a) =>
        a.ClientUserId is { } id ? id.ToString() : GuestPrefix + (a.ClientPhone ?? a.ClientName);

    private static async Task<Ok<List<ClientSummaryDto>>> ListAsync(
        string? search, AppDbContext db, IUserDirectory users, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var cancelled = AppointmentStatus.Cancelled;

        var registered = await db.Set<Appointment>().AsNoTracking()
            .Where(a => a.ClientUserId != null)
            .GroupBy(a => a.ClientUserId!.Value)
            .Select(g => new Aggregate(
                g.Key.ToString(), null, null,
                g.Count(a => a.Status != cancelled && a.StartsAt < now),
                g.Count(a => a.Status == cancelled),
                g.Where(a => a.Status != cancelled && a.StartsAt < now).Sum(a => (decimal?)a.Price) ?? 0,
                g.Where(a => a.Status != cancelled && a.StartsAt < now).Max(a => (DateTimeOffset?)a.StartsAt),
                g.Where(a => a.Status != cancelled && a.StartsAt >= now).Min(a => (DateTimeOffset?)a.StartsAt),
                g.Key))
            .ToListAsync(ct);

        var guests = await db.Set<Appointment>().AsNoTracking()
            .Where(a => a.ClientUserId == null)
            .GroupBy(a => a.ClientPhone ?? a.ClientName)
            .Select(g => new Aggregate(
                GuestPrefix + g.Key,
                g.OrderByDescending(a => a.StartsAt).Select(a => a.ClientName).First(),
                g.Max(a => a.ClientPhone),
                g.Count(a => a.Status != cancelled && a.StartsAt < now),
                g.Count(a => a.Status == cancelled),
                g.Where(a => a.Status != cancelled && a.StartsAt < now).Sum(a => (decimal?)a.Price) ?? 0,
                g.Where(a => a.Status != cancelled && a.StartsAt < now).Max(a => (DateTimeOffset?)a.StartsAt),
                g.Where(a => a.Status != cancelled && a.StartsAt >= now).Min(a => (DateTimeOffset?)a.StartsAt),
                null))
            .ToListAsync(ct);

        var profiles = await users.GetByIdsAsync(registered.Select(r => r.UserId!.Value).ToList(), ct);

        var clients = registered.Select(r => ToSummary(r, profiles.GetValueOrDefault(r.UserId!.Value)))
            .Concat(guests.Select(g => ToSummary(g, null)))
            .Where(c => Matches(c, search))
            .OrderByDescending(c => c.NextAppointment ?? c.LastVisit)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return TypedResults.Ok(clients);
    }

    private static async Task<Results<Ok<ClientDetailDto>, NotFound>> GetAsync(
        string key, AppDbContext db, IUserDirectory users, TimeProvider clock, CancellationToken ct)
    {
        var query = db.Set<Appointment>().AsNoTracking();
        UserSummary? profile = null;
        if (key.StartsWith(GuestPrefix, StringComparison.Ordinal))
        {
            var value = key[GuestPrefix.Length..];
            query = query.Where(a => a.ClientUserId == null && (a.ClientPhone ?? a.ClientName) == value);
        }
        else if (Guid.TryParse(key, out var userId))
        {
            query = query.Where(a => a.ClientUserId == userId);
            profile = (await users.GetByIdsAsync([userId], ct)).GetValueOrDefault(userId);
        }
        else
        {
            return TypedResults.NotFound();
        }

        var appointments = await query.OrderByDescending(a => a.StartsAt).ToListAsync(ct);
        if (appointments.Count == 0)
        {
            return TypedResults.NotFound();
        }

        var staffIds = appointments.Select(a => a.StaffMemberId).Distinct().ToList();
        var locationIds = appointments.Select(a => a.LocationId).Distinct().ToList();
        var staff = await db.Set<StaffMember>().AsNoTracking().Where(s => staffIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.DisplayName, ct);
        var locations = await db.Set<Location>().AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);

        var now = clock.GetUtcNow();
        var active = appointments.Where(a => a.Status != AppointmentStatus.Cancelled).ToList();
        var past = active.Where(a => a.StartsAt < now).ToList();
        var latestGuest = appointments[0];
        var summary = ToSummary(
            new Aggregate(
                key, latestGuest.ClientName, appointments.Select(a => a.ClientPhone).FirstOrDefault(p => p is not null),
                past.Count, appointments.Count - active.Count, past.Sum(a => a.Price),
                past.Count == 0 ? null : past.Max(a => a.StartsAt),
                active.Where(a => a.StartsAt >= now).Select(a => (DateTimeOffset?)a.StartsAt).Min(),
                latestGuest.ClientUserId),
            profile);

        return TypedResults.Ok(new ClientDetailDto(
            summary,
            appointments.Select(a => new ClientAppointmentDto(
                a.Id, a.StartsAt, a.EndsAt, a.ServiceName, a.Price,
                staff.GetValueOrDefault(a.StaffMemberId, string.Empty),
                locations.TryGetValue(a.LocationId, out var l) ? l.Name : string.Empty,
                locations.TryGetValue(a.LocationId, out var tz) ? tz.TimeZone : "UTC",
                a.Status)).ToList()));
    }

    private static ClientSummaryDto ToSummary(Aggregate a, UserSummary? profile) => new(
        a.Key,
        profile?.DisplayName ?? a.Name,
        profile?.Email,
        profile?.PhoneNumber ?? a.Phone,
        a.UserId is null,
        a.Visits, a.Cancelled, a.Spent, a.LastVisit, a.NextAppointment);

    private static bool Matches(ClientSummaryDto c, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var term = search.Trim();
        return new[] { c.Name, c.Email, c.Phone }.Any(v => v?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true);
    }

    private sealed record Aggregate(
        string Key, string? Name, string? Phone, int Visits, int Cancelled, decimal Spent,
        DateTimeOffset? LastVisit, DateTimeOffset? NextAppointment, Guid? UserId);
}
