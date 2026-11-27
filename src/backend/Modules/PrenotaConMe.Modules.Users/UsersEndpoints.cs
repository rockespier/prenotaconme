using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Users.Domain;
using System.Text.Json;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Hosting;
using PrenotaConMe.Shared.Privacy;
using PrenotaConMe.Shared.Tenancy;
using PrenotaConMe.Shared.Validation;

namespace PrenotaConMe.Modules.Users;

public sealed record MembershipDto(Guid TenantId, string TenantSlug, string TenantName, TenantRole Role);

public sealed record MeDto(
    Guid Id, string? Email, string? DisplayName, string? PhotoUrl, string? PhoneNumber, IReadOnlyList<MembershipDto> Memberships,
    bool IsPlatformAdmin);

public sealed record SetPhoneRequest(string PhoneNumber);

internal static class UsersEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        // Called by the frontend right after sign-in: provisions the local user and lists their businesses.
        // F1-10: asked on the first booking; needed for reminders (WhatsApp/Telegram) and to reach the client.
        group.MapPut("/me/phone", SetPhoneAsync)
            .RequireAuthorization()
            .WithName("SetMyPhone")
            .RequireRateLimiting(RateLimits.Sensitive);

        group.MapGet("/me", GetMeAsync)
            .RequireAuthorization()
            .WithName("GetMe");

        // F5-04 (GDPR art. 15/20): everything PrenotaConMe keeps about the caller, as a JSON file.
        group.MapGet("/me/export", ExportAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Sensitive)
            .WithName("ExportMyData");

        // F5-04 (GDPR art. 17): deletes the account; appointments stay for the businesses, anonymized.
        group.MapDelete("/me", DeleteMeAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Sensitive)
            .WithName("DeleteMe");
    }

    private static async Task<FileContentHttpResult> ExportAsync(
        ICurrentUser currentUser, AppDbContext db, ITenantDirectory tenants, IEnumerable<IPersonalData> sources,
        TimeProvider clock, CancellationToken ct)
    {
        var userId = await currentUser.GetUserIdAsync(ct);
        var user = await db.Set<UserAccount>().AsNoTracking().SingleAsync(u => u.Id == userId, ct);
        var memberships = await db.Set<UserMembership>().IgnoreQueryFilters([AppDbContext.TenantFilter]).AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new { m.TenantId, m.Role, m.CreatedAt })
            .ToListAsync(ct);
        var businesses = await tenants.GetByIdsAsync(memberships.Select(m => m.TenantId).Distinct().ToList(), ct);

        var export = new Dictionary<string, object?>
        {
            ["exportedAt"] = clock.GetUtcNow(),
            ["account"] = new { user.Email, user.DisplayName, user.PhoneNumber, user.PhotoUrl, user.CreatedAt },
            ["businesses"] = memberships.Select(m => new
            {
                Business = businesses.TryGetValue(m.TenantId, out var b) ? b.Name : null,
                Role = m.Role.ToString(),
                Since = m.CreatedAt,
            }),
        };
        foreach (var source in sources)
        {
            export[source.Section] = await source.ExportAsync(userId, ct);
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(export, ExportJson);
        return TypedResults.File(json, "application/json", "prenotaconme-data.json");
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteMeAsync(
        ICurrentUser currentUser, AppDbContext db, IEnumerable<IPersonalData> sources, CancellationToken ct)
    {
        var userId = await currentUser.GetUserIdAsync(ct);

        // Admins and staff hold business data (and may be the only admin): they must hand over first.
        var worksSomewhere = await db.Set<UserMembership>().IgnoreQueryFilters([AppDbContext.TenantFilter])
            .AnyAsync(m => m.UserId == userId && m.Role != TenantRole.Client, ct);
        if (worksSomewhere)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "account.hasBusinessRole");
        }

        foreach (var source in sources)
        {
            await source.EraseAsync(userId, ct);
        }

        // Memberships, chats, Telegram links, devices and reminder choices go by cascade.
        db.Remove(await db.Set<UserAccount>().SingleAsync(u => u.Id == userId, ct));
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static async Task<Results<NoContent, ValidationProblem>> SetPhoneAsync(
        SetPhoneRequest request, ICurrentUser currentUser, AppDbContext db, CancellationToken cancellationToken)
    {
        var phone = PhoneNumbers.Normalize(request.PhoneNumber);
        if (phone is null)
        {
            return new ValidationErrors().Add("phoneNumber", "phone.invalid").ToProblem();
        }

        var userId = await currentUser.GetUserIdAsync(cancellationToken);
        var user = await db.Set<UserAccount>().SingleAsync(u => u.Id == userId, cancellationToken);
        user.SetPhoneNumber(phone);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<MeDto>> GetMeAsync(
        ICurrentUser currentUser,
        AppDbContext db,
        ITenantDirectory tenants,
        IEnumerable<IUserSignInHook> signInHooks,
        PlatformAdmins platformAdmins,
        CancellationToken cancellationToken)
    {
        var userId = await currentUser.GetUserIdAsync(cancellationToken);

        // E.g. link pending staff invitations sent to this e-mail.
        var verifiedEmail = currentUser.EmailVerified ? currentUser.Email?.Trim().ToLowerInvariant() : null;
        foreach (var hook in signInHooks)
        {
            await hook.OnSignedInAsync(userId, verifiedEmail, cancellationToken);
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var user = await db.Set<UserAccount>().AsNoTracking().SingleAsync(u => u.Id == userId, cancellationToken);

        // Platform-level read: the user's memberships across all tenants.
        var memberships = await db.Set<UserMembership>()
            .IgnoreQueryFilters([AppDbContext.TenantFilter])
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new { m.TenantId, m.Role })
            .ToListAsync(cancellationToken);

        var tenantInfo = await tenants.GetByIdsAsync(memberships.Select(m => m.TenantId).Distinct().ToList(), cancellationToken);

        var dtos = memberships
            .Where(m => tenantInfo.TryGetValue(m.TenantId, out var t) && t.IsActive)
            .Select(m => new MembershipDto(m.TenantId, tenantInfo[m.TenantId].Slug, tenantInfo[m.TenantId].Name, m.Role))
            .OrderBy(m => m.TenantName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(m => m.Role)
            .ToList();

        return TypedResults.Ok(new MeDto(user.Id, user.Email, user.DisplayName, user.PhotoUrl, user.PhoneNumber, dtos, platformAdmins.IsAdmin(currentUser)));
    }
}
