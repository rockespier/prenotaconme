using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Scheduling;

namespace PrenotaConMe.Modules.Locations;

public sealed record OpeningHoursDto(DayOfWeek Day, TimeOnly Opens, TimeOnly Closes);

public sealed record LocationDto(
    Guid Id, string Name, string? Address, string? City, string? Phone, string TimeZone, bool IsActive,
    IReadOnlyList<OpeningHoursDto> OpeningHours);

public sealed record SaveLocationRequest(
    string Name, string? Address, string? City, string? Phone, string TimeZone, bool IsActive,
    IReadOnlyList<OpeningHoursDto>? OpeningHours);

internal static class LocationsEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/", ListAsync).RequireTenantRole(TenantRole.Admin, TenantRole.Staff).WithName("ListLocations");
        group.MapGet("/{id:guid}", GetAsync).RequireTenantRole(TenantRole.Admin, TenantRole.Staff).WithName("GetLocation");
        group.MapPost("/", CreateAsync).RequireTenantRole(TenantRole.Admin).WithName("CreateLocation");
        group.MapPut("/{id:guid}", UpdateAsync).RequireTenantRole(TenantRole.Admin).WithName("UpdateLocation");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireTenantRole(TenantRole.Admin).WithName("DeleteLocation");
    }

    private static async Task<Ok<List<LocationDto>>> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var locations = await db.Set<Location>().AsNoTracking().OrderBy(l => l.Name).ToListAsync(ct);
        return TypedResults.Ok(locations.Select(ToDto).ToList());
    }

    private static async Task<Results<Ok<LocationDto>, NotFound>> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var location = await db.Set<Location>().AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, ct);
        return location is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(location));
    }

    private static async Task<Results<Created<LocationDto>, ValidationProblem>> CreateAsync(
        SaveLocationRequest request, AppDbContext db, CancellationToken ct)
    {
        var errors = LocationRules.Validate(request);
        if (!errors.IsValid)
        {
            return errors.ToProblem();
        }

        var location = Location.Create(ToData(request));
        db.Add(location);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/locations/{location.Id}", ToDto(location));
    }

    private static async Task<Results<Ok<LocationDto>, NotFound, ValidationProblem>> UpdateAsync(
        Guid id, SaveLocationRequest request, AppDbContext db, CancellationToken ct)
    {
        var errors = LocationRules.Validate(request);
        if (!errors.IsValid)
        {
            return errors.ToProblem();
        }

        var location = await db.Set<Location>().SingleOrDefaultAsync(l => l.Id == id, ct);
        if (location is null)
        {
            return TypedResults.NotFound();
        }

        location.Update(ToData(request));
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToDto(location));
    }

    /// <summary>Also removes the location from services and staff, and its staff working hours (DB cascade).</summary>
    private static async Task<Results<NoContent, NotFound>> DeleteAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var deleted = await db.Set<Location>().Where(l => l.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static LocationData ToData(SaveLocationRequest r) => new(
        r.Name, r.Address, r.City, r.Phone, r.TimeZone, r.IsActive,
        (r.OpeningHours ?? []).Select(h => new WeeklyInterval(h.Day, h.Opens, h.Closes)).ToList());

    internal static LocationDto ToDto(Location l) => new(
        l.Id, l.Name, l.Address, l.City, l.Phone, l.TimeZone, l.IsActive,
        l.OpeningHours.Select(h => new OpeningHoursDto(h.Day, h.Opens, h.Closes)).ToList());
}
