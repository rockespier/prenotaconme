using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Tenancy.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Tenancy;
using PrenotaConMe.Shared.Validation;
using PrenotaConMe.Shared.Hosting;

namespace PrenotaConMe.Modules.Tenancy;

public sealed record RegisterTenantRequest(string Name, string Slug, string TimeZone, string Language);

/// <param name="FirstReminderHours">Hours before the appointment of the first reminder to the client (0 = off).</param>
/// <param name="SecondReminderHours">Hours before the appointment of the second reminder (0 = off).</param>
/// <param name="GoogleCalendarSync">F2-07: staff may connect Google Calendar.</param>
/// <param name="LogoUrl">F5-08: relative URL of the logo, null without one.</param>
/// <param name="CoverUrl">F5-08: relative URL of the cover image, null without one.</param>
public sealed record TenantDto(
    Guid Id, string Name, string Slug, string TimeZone, string DefaultLanguage, int CancellationNoticeHours,
    int FirstReminderHours, int SecondReminderHours, bool GoogleCalendarSync, string? LogoUrl, string? CoverUrl);

public sealed record PublicTenantDto(
    string Name, string Slug, string TimeZone, string DefaultLanguage, int CancellationNoticeHours, string? LogoUrl, string? CoverUrl);

/// <summary>Only the values sent are changed.</summary>
public sealed record UpdateTenantSettingsRequest(
    int? CancellationNoticeHours = null, int? FirstReminderHours = null, int? SecondReminderHours = null,
    bool? GoogleCalendarSync = null);

public sealed record SlugAvailabilityDto(string Slug, bool Available, string? Reason);

internal static class TenancyEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        // F1-01: a signed-in user registers a business and becomes its Admin.
        group.MapPost("/tenants", RegisterAsync)
            .RequireAuthorization()
            .WithName("RegisterTenant")
            .RequireRateLimiting(RateLimits.Sensitive);

        group.MapGet("/tenants/{slug}", GetPublicAsync)
            .AllowAnonymous()
            .WithName("GetPublicTenant");

        group.MapGet("/slugs/{slug}/availability", CheckSlugAsync)
            .AllowAnonymous()
            .WithName("CheckSlugAvailability");

        group.MapGet("/tenants/current", GetCurrentAsync)
            .RequireTenantRole(TenantRole.Admin, TenantRole.Staff)
            .WithName("GetCurrentTenant");

        // F1-12: business settings (cancellation/reschedule notice).
        group.MapPut("/tenants/current/settings", UpdateSettingsAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("UpdateTenantSettings");

        TenantImageEndpoints.Map(group);
    }

    private static async Task<Results<Created<TenantDto>, ValidationProblem, ProblemHttpResult>> RegisterAsync(
        RegisterTenantRequest request,
        AppDbContext db,
        ICurrentUser currentUser,
        IMembershipWriter memberships,
        IEnumerable<ITenantInitializer> initializers,
        CancellationToken cancellationToken)
    {
        var slug = TenantRules.NormalizeSlug(request.Slug);
        var name = request.Name?.Trim() ?? string.Empty;
        var slugError = TenantRules.ValidateSlug(slug);

        var errors = new ValidationErrors()
            .AddIf(name.Length == 0, "name", "name.required")
            .AddIf(name.Length > TenantRules.NameMaxLength, "name", "name.length")
            .AddIf(slugError is not null, "slug", slugError!)
            .AddIf(!TenantRules.IsValidTimeZone(request.TimeZone), "timeZone", "timeZone.invalid")
            .AddIf(!TenantRules.IsValidLanguage(request.Language), "language", "language.invalid");

        if (!errors.IsValid)
        {
            return errors.ToProblem();
        }

        if (await db.Set<Tenant>().AnyAsync(t => t.Slug == slug, cancellationToken))
        {
            return SlugTaken(slug);
        }

        var userId = await currentUser.GetUserIdAsync(cancellationToken);
        var tenant = Tenant.Create(name, slug, request.TimeZone, request.Language);

        db.Add(tenant);
        await memberships.EnsureAsync(tenant.Id, userId, TenantRole.Admin, cancellationToken);

        // Starter data from other modules (default location, sample services), committed atomically below.
        var initContext = new TenantInitializationContext(tenant.Id, tenant.TimeZone, tenant.DefaultLanguage, userId);
        foreach (var initializer in initializers.OrderBy(i => i.Order))
        {
            await initializer.InitializeAsync(initContext, cancellationToken);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Lost a race with another registration for the same slug.
            return SlugTaken(slug);
        }

        return TypedResults.Created($"/api/tenancy/tenants/{tenant.Slug}", ToDto(tenant));
    }

    private static async Task<Results<Ok<PublicTenantDto>, NotFound>> GetPublicAsync(
        string slug, AppDbContext db, CancellationToken cancellationToken)
    {
        var normalized = TenantRules.NormalizeSlug(slug);
        var tenant = await db.Set<Tenant>()
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Slug == normalized && t.IsActive, cancellationToken);

        return tenant is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new PublicTenantDto(
                tenant.Name, tenant.Slug, tenant.TimeZone, tenant.DefaultLanguage, tenant.CancellationNoticeHours,
                TenantImageEndpoints.Url(tenant, TenantImageKind.Logo), TenantImageEndpoints.Url(tenant, TenantImageKind.Cover)));
    }

    private static async Task<Ok<SlugAvailabilityDto>> CheckSlugAsync(
        string slug, AppDbContext db, CancellationToken cancellationToken)
    {
        var normalized = TenantRules.NormalizeSlug(slug);
        var error = TenantRules.ValidateSlug(normalized);

        if (error is null && await db.Set<Tenant>().AnyAsync(t => t.Slug == normalized, cancellationToken))
        {
            error = "slug.taken";
        }

        return TypedResults.Ok(new SlugAvailabilityDto(normalized, error is null, error));
    }

    private static async Task<Ok<TenantDto>> GetCurrentAsync(
        ITenantContext tenantContext, AppDbContext db, CancellationToken cancellationToken)
    {
        var tenant = await db.Set<Tenant>()
            .AsNoTracking()
            .SingleAsync(t => t.Id == tenantContext.TenantId, cancellationToken);

        return TypedResults.Ok(ToDto(tenant));
    }

    private static async Task<Results<Ok<TenantDto>, ValidationProblem>> UpdateSettingsAsync(
        UpdateTenantSettingsRequest request, ITenantContext tenantContext, AppDbContext db, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors()
            .AddIf(request.CancellationNoticeHours is < 0 or > TenantRules.MaxCancellationNoticeHours, "cancellationNoticeHours", "cancellationNotice.range")
            .AddIf(request.FirstReminderHours is < 0 or > TenantRules.MaxReminderHours, "firstReminderHours", "reminder.range")
            .AddIf(request.SecondReminderHours is < 0 or > TenantRules.MaxReminderHours, "secondReminderHours", "reminder.range");
        if (!errors.IsValid)
        {
            return errors.ToProblem();
        }

        var tenant = await db.Set<Tenant>().SingleAsync(t => t.Id == tenantContext.TenantId, cancellationToken);
        if (request.CancellationNoticeHours is { } notice)
        {
            tenant.SetCancellationNotice(notice);
        }

        if (request.FirstReminderHours is not null || request.SecondReminderHours is not null)
        {
            tenant.SetReminders(
                request.FirstReminderHours ?? tenant.FirstReminderHours, request.SecondReminderHours ?? tenant.SecondReminderHours);
        }

        if (request.GoogleCalendarSync is { } sync)
        {
            tenant.SetGoogleCalendarSync(sync);
        }

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToDto(tenant));
    }

    private static ProblemHttpResult SlugTaken(string slug) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "slug.taken",
            detail: $"The slug '{slug}' is already in use.");

    internal static TenantDto ToDto(Tenant t) =>
        new(t.Id, t.Name, t.Slug, t.TimeZone, t.DefaultLanguage, t.CancellationNoticeHours, t.FirstReminderHours, t.SecondReminderHours,
            t.GoogleCalendarSync, TenantImageEndpoints.Url(t, TenantImageKind.Logo), TenantImageEndpoints.Url(t, TenantImageKind.Cover));
}
