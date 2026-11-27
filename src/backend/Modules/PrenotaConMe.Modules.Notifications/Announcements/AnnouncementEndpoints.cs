using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Notifications.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Tenancy;
using PrenotaConMe.Shared.Hosting;

namespace PrenotaConMe.Modules.Notifications.Announcements;

public sealed record AnnouncementDto(
    Guid Id, string Title, string Message, DateOnly StartsOn, DateOnly EndsOn, DateTimeOffset? SentAt, int? RecipientCount);

public sealed record PublicAnnouncementDto(Guid Id, string Title, string Message, DateOnly EndsOn);

/// <param name="Clients">Clients of the business (they get push where enabled).</param>
/// <param name="WithEmail">Clients with an e-mail address.</param>
public sealed record AnnouncementAudienceDto(int Clients, int WithEmail);

/// <param name="Notify">Also send it now to every client by push and e-mail (only once).</param>
public sealed record CreateAnnouncementRequest(string Title, string Message, DateOnly StartsOn, DateOnly EndsOn, bool Notify);

public sealed record UpdateAnnouncementRequest(string Title, string Message, DateOnly StartsOn, DateOnly EndsOn);

/// <summary>F2-03: Admin announcements (A→C) and the banner on the public pages.</summary>
internal static class AnnouncementEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/announcements/current", CurrentAsync)
            .RequireTenant()
            .AllowAnonymous()
            .WithName("GetCurrentAnnouncements");

        group.MapGet("/announcements", ListAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("ListAnnouncements");

        group.MapGet("/announcements/audience", AudienceAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("GetAnnouncementAudience");

        group.MapPost("/announcements", CreateAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("CreateAnnouncement")
            .RequireRateLimiting(RateLimits.Sensitive);

        group.MapPut("/announcements/{id:guid}", UpdateAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("UpdateAnnouncement");

        group.MapPost("/announcements/{id:guid}/send", SendAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("SendAnnouncement")
            .RequireRateLimiting(RateLimits.Sensitive);

        group.MapDelete("/announcements/{id:guid}", DeleteAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("DeleteAnnouncement");
    }

    /// <summary>Announcements to show today (business-local date).</summary>
    private static async Task<Ok<List<PublicAnnouncementDto>>> CurrentAsync(
        AppDbContext db, ITenantContext tenant, ITenantDirectory tenants, TimeProvider clock, CancellationToken ct)
    {
        var today = await TodayAsync(tenant, tenants, clock, ct);
        var list = await db.Set<Announcement>().AsNoTracking()
            .Where(a => a.StartsOn <= today && a.EndsOn >= today)
            .OrderBy(a => a.EndsOn).ThenBy(a => a.Id)
            .Select(a => new PublicAnnouncementDto(a.Id, a.Title, a.Message, a.EndsOn))
            .ToListAsync(ct);
        return TypedResults.Ok(list);
    }

    private static async Task<Ok<List<AnnouncementDto>>> ListAsync(AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Set<Announcement>().AsNoTracking()
            .OrderByDescending(a => a.StartsOn).ThenByDescending(a => a.Id)
            .Select(a => new AnnouncementDto(a.Id, a.Title, a.Message, a.StartsOn, a.EndsOn, a.SentAt, a.RecipientCount))
            .ToListAsync(ct));

    private static async Task<Ok<AnnouncementAudienceDto>> AudienceAsync(AppDbContext db, CancellationToken ct)
    {
        var recipients = await AnnouncementJob.RecipientsAsync(db, ct);
        return TypedResults.Ok(new AnnouncementAudienceDto(recipients.Count, recipients.Count(r => !string.IsNullOrWhiteSpace(r.Email))));
    }

    private static async Task<Results<Created<AnnouncementDto>, ValidationProblem>> CreateAsync(
        CreateAnnouncementRequest request, AppDbContext db, IBackgroundJobClient jobs, TimeProvider clock, CancellationToken ct)
    {
        var errors = Announcement.Validate(request.Title, request.Message, request.StartsOn, request.EndsOn);
        if (!errors.IsValid)
        {
            return errors.ToProblem();
        }

        var announcement = Announcement.Create(request.Title, request.Message, request.StartsOn, request.EndsOn);
        db.Add(announcement);
        await db.SaveChangesAsync(ct);
        if (request.Notify)
        {
            await SendNowAsync(announcement, db, jobs, clock, ct);
        }

        return TypedResults.Created($"/api/notifications/announcements/{announcement.Id}", ToDto(announcement));
    }

    private static async Task<Results<Ok<AnnouncementDto>, NotFound, ValidationProblem>> UpdateAsync(
        Guid id, UpdateAnnouncementRequest request, AppDbContext db, CancellationToken ct)
    {
        var announcement = await db.Set<Announcement>().SingleOrDefaultAsync(a => a.Id == id, ct);
        if (announcement is null)
        {
            return TypedResults.NotFound();
        }

        var errors = Announcement.Validate(request.Title, request.Message, request.StartsOn, request.EndsOn);
        if (!errors.IsValid)
        {
            return errors.ToProblem();
        }

        announcement.Update(request.Title, request.Message, request.StartsOn, request.EndsOn);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(announcement));
    }

    /// <summary>Sends a saved announcement; each announcement is sent at most once (409 announcement.alreadySent).</summary>
    private static async Task<Results<Ok<AnnouncementDto>, NotFound, ProblemHttpResult>> SendAsync(
        Guid id, AppDbContext db, IBackgroundJobClient jobs, TimeProvider clock, CancellationToken ct)
    {
        var announcement = await db.Set<Announcement>().SingleOrDefaultAsync(a => a.Id == id, ct);
        if (announcement is null)
        {
            return TypedResults.NotFound();
        }

        if (announcement.SentAt is not null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "announcement.alreadySent",
                detail: "This announcement was already sent.");
        }

        await SendNowAsync(announcement, db, jobs, clock, ct);
        return TypedResults.Ok(ToDto(announcement));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(Guid id, AppDbContext db, CancellationToken ct) =>
        await db.Set<Announcement>().Where(a => a.Id == id).ExecuteDeleteAsync(ct) == 0
            ? TypedResults.NotFound()
            : TypedResults.NoContent();

    /// <summary>
    /// Marks it sent (saved first, so a second click cannot send it twice and the job finds the row), then queues
    /// the job. If queueing fails, the mark is undone so the Admin can try again.
    /// </summary>
    private static async Task SendNowAsync(Announcement announcement, AppDbContext db, IBackgroundJobClient jobs, TimeProvider clock, CancellationToken ct)
    {
        var recipients = await AnnouncementJob.RecipientsAsync(db, ct);
        announcement.MarkSent(clock.GetUtcNow(), recipients.Count);
        await db.SaveChangesAsync(ct);

        var id = announcement.Id;
        try
        {
            jobs.Enqueue<AnnouncementJob>(j => j.SendAsync(id, CancellationToken.None));
        }
        catch
        {
            announcement.ClearSent();
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<DateOnly> TodayAsync(ITenantContext tenant, ITenantDirectory tenants, TimeProvider clock, CancellationToken ct)
    {
        var id = tenant.TenantId!.Value;
        var business = (await tenants.GetByIdsAsync([id], ct))[id];
        var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }

    private static AnnouncementDto ToDto(Announcement a) =>
        new(a.Id, a.Title, a.Message, a.StartsOn, a.EndsOn, a.SentAt, a.RecipientCount);
}
