using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Catalog.Domain;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Modules.Staff.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Scheduling;
using PrenotaConMe.Shared.Tenancy;
using PrenotaConMe.Shared.Validation;

namespace PrenotaConMe.Modules.Staff;

public sealed record StaffDto(
    Guid Id, string DisplayName, string Email, bool IsActive, bool IsLinked,
    IReadOnlyList<Guid> LocationIds, IReadOnlyList<Guid> ServiceIds);

public sealed record SaveStaffRequest(
    string DisplayName, string Email, bool IsActive, IReadOnlyList<Guid>? LocationIds, IReadOnlyList<Guid>? ServiceIds);

public sealed record WorkingHoursDto(Guid LocationId, DayOfWeek Day, TimeOnly Start, TimeOnly End);

public sealed record TimeOffDto(Guid Id, DateOnly From, DateOnly To, string? Reason);

public sealed record AddTimeOffRequest(DateOnly From, DateOnly To, string? Reason);

public sealed record StaffScheduleDto(IReadOnlyList<WorkingHoursDto> WorkingHours, IReadOnlyList<TimeOffDto> TimeOff);

internal static class StaffEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/", ListAsync).RequireTenantRole(TenantRole.Admin, TenantRole.Staff).WithName("ListStaff");
        group.MapGet("/{id:guid}", GetAsync).RequireTenantRole(TenantRole.Admin, TenantRole.Staff).WithName("GetStaff");
        group.MapPost("/", CreateAsync).RequireTenantRole(TenantRole.Admin).WithName("CreateStaff");
        group.MapPut("/{id:guid}", UpdateAsync).RequireTenantRole(TenantRole.Admin).WithName("UpdateStaff");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireTenantRole(TenantRole.Admin).WithName("DeleteStaff");

        group.MapGet("/{id:guid}/schedule", GetScheduleAsync).RequireTenantRole(TenantRole.Admin, TenantRole.Staff).WithName("GetStaffSchedule");
        group.MapPut("/{id:guid}/working-hours", ReplaceWorkingHoursAsync).RequireTenantRole(TenantRole.Admin).WithName("ReplaceStaffWorkingHours");
        group.MapPost("/{id:guid}/time-off", AddTimeOffAsync).RequireTenantRole(TenantRole.Admin).WithName("AddStaffTimeOff");
        group.MapDelete("/{id:guid}/time-off/{timeOffId:guid}", DeleteTimeOffAsync).RequireTenantRole(TenantRole.Admin).WithName("DeleteStaffTimeOff");
    }

    private static IQueryable<StaffMember> WithLinks(AppDbContext db) =>
        db.Set<StaffMember>().Include(s => s.Locations).Include(s => s.Services);

    private static async Task<Ok<List<StaffDto>>> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var staff = await WithLinks(db).AsNoTracking().OrderBy(s => s.DisplayName).ToListAsync(ct);
        return TypedResults.Ok(staff.Select(ToDto).ToList());
    }

    private static async Task<Results<Ok<StaffDto>, NotFound>> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var staff = await WithLinks(db).AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);
        return staff is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(staff));
    }

    private static async Task<Results<Created<StaffDto>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        SaveStaffRequest request, AppDbContext db, ICurrentUser currentUser, IMembershipWriter memberships,
        ITenantContext tenant, CancellationToken ct)
    {
        var errors = await ValidateAsync(request, db, ct);
        if (!errors.IsValid)
        {
            return errors.ToProblem();
        }

        var staff = StaffMember.Create(ToData(request));
        db.Add(staff);

        // An Admin adding themselves (same verified e-mail) is linked right away.
        if (currentUser.EmailVerified && StaffRules.NormalizeEmail(currentUser.Email) == staff.Email)
        {
            var userId = await currentUser.GetUserIdAsync(ct);
            staff.LinkUser(userId);
            await memberships.EnsureAsync(tenant.TenantId!.Value, userId, TenantRole.Staff, ct);
        }

        if (await TrySaveAsync(db, ct) is { } conflict)
        {
            return conflict;
        }

        return TypedResults.Created($"/api/staff/{staff.Id}", ToDto(staff));
    }

    private static async Task<Results<Ok<StaffDto>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid id, SaveStaffRequest request, AppDbContext db, CancellationToken ct)
    {
        var errors = await ValidateAsync(request, db, ct);
        if (!errors.IsValid)
        {
            return errors.ToProblem();
        }

        var staff = await WithLinks(db).Include(s => s.WorkingHours).SingleOrDefaultAsync(s => s.Id == id, ct);
        if (staff is null)
        {
            return TypedResults.NotFound();
        }

        if (!staff.TryChangeEmail(StaffRules.NormalizeEmail(request.Email)))
        {
            return new ValidationErrors().Add("email", "email.locked").ToProblem();
        }

        staff.Update(ToData(request));

        if (await TrySaveAsync(db, ct) is { } conflict)
        {
            return conflict;
        }

        return TypedResults.Ok(ToDto(staff));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id, AppDbContext db, IMembershipWriter memberships, ITenantContext tenant, CancellationToken ct)
    {
        var staff = await db.Set<StaffMember>().SingleOrDefaultAsync(s => s.Id == id, ct);
        if (staff is null)
        {
            return TypedResults.NotFound();
        }

        if (staff.UserId is { } userId)
        {
            await memberships.RemoveAsync(tenant.TenantId!.Value, userId, TenantRole.Staff, ct);
        }

        db.Remove(staff);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<StaffScheduleDto>, NotFound>> GetScheduleAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var staff = await db.Set<StaffMember>().AsNoTracking()
            .Include(s => s.WorkingHours).Include(s => s.TimeOff)
            .SingleOrDefaultAsync(s => s.Id == id, ct);

        return staff is null ? TypedResults.NotFound() : TypedResults.Ok(ToScheduleDto(staff));
    }

    private static async Task<Results<Ok<StaffScheduleDto>, NotFound, ValidationProblem>> ReplaceWorkingHoursAsync(
        Guid id, IReadOnlyList<WorkingHoursDto> hours, AppDbContext db, CancellationToken ct)
    {
        var staff = await db.Set<StaffMember>()
            .Include(s => s.Locations).Include(s => s.WorkingHours).Include(s => s.TimeOff)
            .SingleOrDefaultAsync(s => s.Id == id, ct);
        if (staff is null)
        {
            return TypedResults.NotFound();
        }

        var locationIds = staff.Locations.Select(l => l.LocationId).ToHashSet();
        var locations = await db.Set<Location>().AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToListAsync(ct);
        var opening = locations.ToDictionary(l => l.Id, l => (IReadOnlyList<WeeklyInterval>)l.OpeningIntervals.ToList());

        var errors = StaffRules.ValidateWorkingHours(hours, locationIds, opening);
        if (!errors.IsValid)
        {
            return errors.ToProblem();
        }

        staff.ReplaceWorkingHours(hours.Select(h => new StaffWorkingHours(staff.Id, h.LocationId, h.Day, h.Start, h.End)));
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToScheduleDto(staff));
    }

    private static async Task<Results<Created<TimeOffDto>, NotFound, ValidationProblem>> AddTimeOffAsync(
        Guid id, AddTimeOffRequest request, AppDbContext db, CancellationToken ct)
    {
        var errors = StaffRules.ValidateTimeOff(request);
        if (!errors.IsValid)
        {
            return errors.ToProblem();
        }

        if (!await db.Set<StaffMember>().AnyAsync(s => s.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var timeOff = new StaffTimeOff(id, request.From, request.To, reason);
        db.Add(timeOff);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/staff/{id}/time-off/{timeOff.Id}", new TimeOffDto(timeOff.Id, timeOff.From, timeOff.To, timeOff.Reason));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteTimeOffAsync(Guid id, Guid timeOffId, AppDbContext db, CancellationToken ct)
    {
        var deleted = await db.Set<StaffTimeOff>().Where(t => t.Id == timeOffId && t.StaffMemberId == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<ValidationErrors> ValidateAsync(SaveStaffRequest request, AppDbContext db, CancellationToken ct)
    {
        var errors = StaffRules.Validate(request);

        // Tenant filter: ids must belong to this business.
        var locationIds = (request.LocationIds ?? []).Distinct().ToList();
        if (locationIds.Count > 0 && await db.Set<Location>().CountAsync(l => locationIds.Contains(l.Id), ct) != locationIds.Count)
        {
            errors.Add("locationIds", "locations.unknown");
        }

        var serviceIds = (request.ServiceIds ?? []).Distinct().ToList();
        if (serviceIds.Count > 0 && await db.Set<Service>().CountAsync(s => serviceIds.Contains(s.Id), ct) != serviceIds.Count)
        {
            errors.Add("serviceIds", "services.unknown");
        }

        return errors;
    }

    private static async Task<ProblemHttpResult?> TrySaveAsync(AppDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "email.taken",
                detail: "A staff member with this e-mail already exists in this business.");
        }
    }

    private static StaffData ToData(SaveStaffRequest r) => new(
        r.DisplayName, StaffRules.NormalizeEmail(r.Email), r.IsActive,
        (r.LocationIds ?? []).Distinct().ToList(), (r.ServiceIds ?? []).Distinct().ToList());

    private static StaffDto ToDto(StaffMember s) => new(
        s.Id, s.DisplayName, s.Email, s.IsActive, s.UserId is not null,
        s.Locations.Select(l => l.LocationId).ToList(), s.Services.Select(x => x.ServiceId).ToList());

    private static StaffScheduleDto ToScheduleDto(StaffMember s) => new(
        s.WorkingHours
            .OrderBy(h => ((int)h.Day + 6) % 7).ThenBy(h => h.Start)
            .Select(h => new WorkingHoursDto(h.LocationId, h.Day, h.Start, h.End)).ToList(),
        s.TimeOff
            .OrderBy(t => t.From)
            .Select(t => new TimeOffDto(t.Id, t.From, t.To, t.Reason)).ToList());
}
