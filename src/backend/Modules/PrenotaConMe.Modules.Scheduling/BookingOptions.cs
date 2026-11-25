using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Catalog.Domain;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Modules.Staff.Domain;

namespace PrenotaConMe.Modules.Scheduling;

public sealed record PublicLocationDto(Guid Id, string Name, string? Address, string? City, string? Phone, string TimeZone);

public sealed record PublicServiceDto(Guid Id, string Name, string? Description, int DurationMinutes, decimal Price, IReadOnlyList<Guid> LocationIds);

/// <summary>Public view of a staff member: no e-mail or account details.</summary>
public sealed record PublicStaffDto(Guid Id, string DisplayName, IReadOnlyList<Guid> LocationIds, IReadOnlyList<Guid> ServiceIds);

public sealed record BookingOptionsDto(
    IReadOnlyList<PublicLocationDto> Locations, IReadOnlyList<PublicServiceDto> Services, IReadOnlyList<PublicStaffDto> Staff);

/// <summary>F1-10: everything the public booking page needs in one call (only what can actually be booked).</summary>
public static class BookingOptions
{
    internal static async Task<Ok<BookingOptionsDto>> GetAsync(AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await LoadAsync(db, ct));

    /// <summary>Also used by the chatbot (list_services).</summary>
    public static async Task<BookingOptionsDto> LoadAsync(AppDbContext db, CancellationToken ct)
    {
        var locations = await db.Set<Location>().AsNoTracking().Where(l => l.IsActive).OrderBy(l => l.Name).ToListAsync(ct);
        var activeLocationIds = locations.Select(l => l.Id).ToHashSet();

        var services = await db.Set<Service>().AsNoTracking().Include(s => s.Locations)
            .Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync(ct);

        var staff = await db.Set<StaffMember>().AsNoTracking().Include(s => s.Locations).Include(s => s.Services)
            .Where(s => s.IsActive).OrderBy(s => s.DisplayName).ToListAsync(ct);

        return new BookingOptionsDto(
            locations.Select(l => new PublicLocationDto(l.Id, l.Name, l.Address, l.City, l.Phone, l.TimeZone)).ToList(),
            services
                .Select(s => new PublicServiceDto(s.Id, s.Name, s.Description, s.DurationMinutes, s.Price,
                    s.Locations.Select(l => l.LocationId).Where(activeLocationIds.Contains).ToList()))
                .Where(s => s.LocationIds.Count > 0)
                .ToList(),
            staff.Select(s => new PublicStaffDto(s.Id, s.DisplayName,
                    s.Locations.Select(l => l.LocationId).Where(activeLocationIds.Contains).ToList(),
                    s.Services.Select(x => x.ServiceId).ToList()))
                .ToList());
    }
}
