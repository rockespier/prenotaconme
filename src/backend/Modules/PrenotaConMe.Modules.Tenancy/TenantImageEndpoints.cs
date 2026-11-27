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

/// <summary>
/// F5-08: logo and cover of the business. The browser crops and resizes before uploading, so the API only checks
/// the size and the file signature. Image URLs carry the version, so they are cached for a year.
/// </summary>
internal static class TenantImageEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/tenants/{slug}/images/{kind}", GetAsync)
            .AllowAnonymous()
            .WithName("GetTenantImage");

        // Bearer token only (no cookies), so no antiforgery token is needed for the form upload.
        group.MapPut("/tenants/current/images/{kind}", UploadAsync)
            .RequireTenantRole(TenantRole.Admin)
            .DisableAntiforgery()
            .WithName("UploadTenantImage")
            .RequireRateLimiting(RateLimits.Sensitive);

        group.MapDelete("/tenants/current/images/{kind}", DeleteAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("DeleteTenantImage");
    }

    /// <summary>Public URL of the image, or null when the business has none.</summary>
    public static string? Url(Tenant tenant, TenantImageKind kind) =>
        tenant.ImageVersion(kind) is { } version
            ? $"/api/tenancy/tenants/{tenant.Slug}/images/{kind.ToString().ToLowerInvariant()}?v={version}"
            : null;

    /// <summary>Route values are "logo" and "cover" (lower case, as in <see cref="Url"/>).</summary>
    private static bool TryParse(string value, out TenantImageKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind) && Enum.IsDefined(kind) && !int.TryParse(value, out _);

    private static async Task<Results<FileContentHttpResult, NotFound>> GetAsync(
        string slug, string kind, AppDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!TryParse(kind, out var imageKind))
        {
            return TypedResults.NotFound();
        }

        var normalized = TenantRules.NormalizeSlug(slug);
        var image = await (
                from i in db.Set<TenantImage>().AsNoTracking()
                join t in db.Set<Tenant>() on i.TenantId equals t.Id
                where t.Slug == normalized && t.IsActive && i.Kind == imageKind
                select new { i.ContentType, i.Data })
            .SingleOrDefaultAsync(ct);
        if (image is null)
        {
            return TypedResults.NotFound();
        }

        http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(image.Data, image.ContentType);
    }

    private static async Task<Results<Ok<TenantDto>, NotFound, ValidationProblem>> UploadAsync(
        string kind, IFormFile file, ITenantContext tenantContext, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryParse(kind, out var imageKind))
        {
            return TypedResults.NotFound();
        }

        var errors = new ValidationErrors().AddIf(file.Length is 0 or > TenantImageRules.MaxBytes, "file", "image.size");
        if (!errors.IsValid)
        {
            return errors.ToProblem();
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        var data = buffer.ToArray();
        var contentType = TenantImageRules.DetectContentType(data);
        if (contentType is null)
        {
            return new ValidationErrors().AddIf(true, "file", "image.type").ToProblem();
        }

        var tenantId = tenantContext.TenantId!.Value;
        var tenant = await db.Set<Tenant>().SingleAsync(t => t.Id == tenantId, ct);
        var image = await db.Set<TenantImage>().SingleOrDefaultAsync(i => i.TenantId == tenantId && i.Kind == imageKind, ct);
        if (image is null)
        {
            image = TenantImage.Create(tenantId, imageKind);
            db.Add(image);
        }

        image.Replace(contentType, data);
        tenant.SetImageVersion(imageKind, clock.GetUtcNow().ToUnixTimeMilliseconds());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(TenancyEndpoints.ToDto(tenant));
    }

    private static async Task<Results<Ok<TenantDto>, NotFound>> DeleteAsync(
        string kind, ITenantContext tenantContext, AppDbContext db, CancellationToken ct)
    {
        if (!TryParse(kind, out var imageKind))
        {
            return TypedResults.NotFound();
        }

        var tenantId = tenantContext.TenantId!.Value;
        var tenant = await db.Set<Tenant>().SingleAsync(t => t.Id == tenantId, ct);
        await db.Set<TenantImage>().Where(i => i.TenantId == tenantId && i.Kind == imageKind).ExecuteDeleteAsync(ct);
        tenant.SetImageVersion(imageKind, null);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(TenancyEndpoints.ToDto(tenant));
    }
}
