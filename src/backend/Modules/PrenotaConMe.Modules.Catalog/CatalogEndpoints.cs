using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Catalog.Domain;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Validation;

namespace PrenotaConMe.Modules.Catalog;

public sealed record ServiceDto(
    Guid Id, string Name, string? Description, int DurationMinutes, decimal Price, bool IsActive, IReadOnlyList<Guid> LocationIds);

public sealed record SaveServiceRequest(
    string Name, string? Description, int DurationMinutes, decimal Price, bool IsActive, IReadOnlyList<Guid>? LocationIds);

internal static class CatalogEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/services", ListAsync).RequireTenantRole(TenantRole.Admin, TenantRole.Staff).WithName("ListServices");
        group.MapGet("/services/{id:guid}", GetAsync).RequireTenantRole(TenantRole.Admin, TenantRole.Staff).WithName("GetService");
        group.MapPost("/services", CreateAsync).RequireTenantRole(TenantRole.Admin).WithName("CreateService");
        group.MapPut("/services/{id:guid}", UpdateAsync).RequireTenantRole(TenantRole.Admin).WithName("UpdateService");
        group.MapDelete("/services/{id:guid}", DeleteAsync).RequireTenantRole(TenantRole.Admin).WithName("DeleteService");
    }

    private static async Task<Ok<List<ServiceDto>>> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var services = await db.Set<Service>().AsNoTracking().Include(s => s.Locations)
            .OrderBy(s => s.Name).ToListAsync(ct);
        return TypedResults.Ok(services.Select(ToDto).ToList());
    }

    private static async Task<Results<Ok<ServiceDto>, NotFound>> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var service = await db.Set<Service>().AsNoTracking().Include(s => s.Locations).SingleOrDefaultAsync(s => s.Id == id, ct);
        return service is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(service));
    }

    private static async Task<Results<Created<ServiceDto>, ValidationProblem>> CreateAsync(
        SaveServiceRequest request, AppDbContext db, CancellationToken ct)
    {
        var errors = await ValidateAsync(request, db, ct);
        if (!errors.IsValid)
        {
            return errors.ToProblem();
        }

        var service = Service.Create(ToData(request));
        db.Add(service);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/catalog/services/{service.Id}", ToDto(service));
    }

    private static async Task<Results<Ok<ServiceDto>, NotFound, ValidationProblem>> UpdateAsync(
        Guid id, SaveServiceRequest request, AppDbContext db, CancellationToken ct)
    {
        var errors = await ValidateAsync(request, db, ct);
        if (!errors.IsValid)
        {
            return errors.ToProblem();
        }

        var service = await db.Set<Service>().Include(s => s.Locations).SingleOrDefaultAsync(s => s.Id == id, ct);
        if (service is null)
        {
            return TypedResults.NotFound();
        }

        service.Update(ToData(request));
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToDto(service));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        try
        {
            var deleted = await db.Set<Service>().Where(s => s.Id == id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
        }
        catch (Exception ex) when (DbErrors.IsForeignKeyViolation(ex))
        {
            return InUse("service.inUse");
        }
    }

    private static ProblemHttpResult InUse(string code) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: code,
            detail: "It has appointments: deactivate it instead of deleting it.");

    private static async Task<ValidationErrors> ValidateAsync(SaveServiceRequest request, AppDbContext db, CancellationToken ct)
    {
        var errors = ServiceRules.Validate(request);
        var ids = (request.LocationIds ?? []).Distinct().ToList();

        // Tenant filter: only this business's locations count.
        if (ids.Count > 0 && await db.Set<Location>().CountAsync(l => ids.Contains(l.Id), ct) != ids.Count)
        {
            errors.Add("locationIds", "locations.unknown");
        }

        return errors;
    }

    private static ServiceData ToData(SaveServiceRequest r) =>
        new(r.Name, r.Description, r.DurationMinutes, r.Price, r.IsActive, (r.LocationIds ?? []).Distinct().ToList());

    private static ServiceDto ToDto(Service s) => new(
        s.Id, s.Name, s.Description, s.DurationMinutes, s.Price, s.IsActive, s.Locations.Select(l => l.LocationId).ToList());
}
