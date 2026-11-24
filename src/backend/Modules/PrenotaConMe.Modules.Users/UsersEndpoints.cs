using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Users.Domain;
using PrenotaConMe.Shared.Auth;
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
            .WithName("SetMyPhone");

        group.MapGet("/me", GetMeAsync)
            .RequireAuthorization()
            .WithName("GetMe");
    }

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
