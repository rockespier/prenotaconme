using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Catalog.Domain;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff.Domain;
using PrenotaConMe.Shared.Scheduling;

namespace PrenotaConMe.Modules.Scheduling;

public sealed record AvailabilityQuery(Guid LocationId, Guid ServiceId, Guid? StaffId, DateOnly From, DateOnly To);

/// <summary>Either an error code (bookable data missing) or the computed slots.</summary>
public sealed record AvailabilityResult(string? Error, Location? Location, Service? Service, IReadOnlyList<AvailableSlot> Slots);

/// <summary>Loads everything the <see cref="AvailabilityCalculator"/> needs for one location + service (current tenant).</summary>
public sealed class AvailabilityService(AppDbContext db, TimeProvider clock)
{
    public async Task<AvailabilityResult> GetAsync(AvailabilityQuery query, CancellationToken ct)
    {
        var location = await db.Set<Location>().AsNoTracking()
            .SingleOrDefaultAsync(l => l.Id == query.LocationId && l.IsActive, ct);
        if (location is null || !TimeZoneInfo.TryFindSystemTimeZoneById(location.TimeZone, out var timeZone))
        {
            return Fail("location.notFound");
        }

        var service = await db.Set<Service>().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == query.ServiceId && s.IsActive && s.Locations.Any(l => l.LocationId == query.LocationId), ct);
        if (service is null)
        {
            return Fail("service.notAvailable");
        }

        var staff = await db.Set<StaffMember>().AsNoTracking()
            .Where(s => s.IsActive
                        && s.Locations.Any(l => l.LocationId == query.LocationId)
                        && s.Services.Any(x => x.ServiceId == query.ServiceId)
                        && (query.StaffId == null || s.Id == query.StaffId))
            .Include(s => s.WorkingHours.Where(h => h.LocationId == query.LocationId))
            .Include(s => s.TimeOff.Where(t => t.To >= query.From && t.From <= query.To))
            .ToListAsync(ct);

        if (query.StaffId is not null && staff.Count == 0)
        {
            return Fail("staff.notAvailable");
        }

        // A day of margin on both sides covers any UTC offset of the location.
        var staffIds = staff.Select(s => s.Id).ToList();
        var rangeStart = new DateTimeOffset(query.From.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var rangeEnd = new DateTimeOffset(query.To.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var busy = await db.Set<Appointment>().AsNoTracking()
            .Where(a => staffIds.Contains(a.StaffMemberId)
                        && a.Status != AppointmentStatus.Cancelled
                        && a.StartsAt < rangeEnd && a.EndsAt > rangeStart)
            .Select(a => new { a.StaffMemberId, a.StartsAt, a.EndsAt })
            .ToListAsync(ct);

        var input = new AvailabilityInput(
            timeZone,
            location.OpeningIntervals.ToList(),
            service.DurationMinutes,
            query.From,
            query.To,
            clock.GetUtcNow(),
            staff.Select(s => new StaffAvailabilityInput(
                s.Id,
                s.WorkingHours.Select(h => new WeeklyInterval(h.Day, h.Start, h.End)).ToList(),
                s.TimeOff.Select(t => (t.From, t.To)).ToList(),
                busy.Where(b => b.StaffMemberId == s.Id).Select(b => (b.StartsAt, b.EndsAt)).ToList())).ToList());

        return new AvailabilityResult(null, location, service, AvailabilityCalculator.Calculate(input));
    }

    /// <summary>Local calendar date of an instant at a location (used to recompute one day's slots when booking).</summary>
    public static DateOnly LocalDate(DateTimeOffset instant, string timeZoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime);

    private static AvailabilityResult Fail(string error) => new(error, null, null, []);
}
