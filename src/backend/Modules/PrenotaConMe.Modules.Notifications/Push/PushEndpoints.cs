using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Notifications.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Validation;

namespace PrenotaConMe.Modules.Notifications.Push;

/// <param name="PublicKey">VAPID public key for PushManager.subscribe; null when push is not configured.</param>
public sealed record PushConfigDto(string? PublicKey);

/// <summary>The fields of PushSubscription.toJSON() (endpoint, keys.p256dh, keys.auth).</summary>
public sealed record SavePushSubscriptionRequest(string Endpoint, string P256dh, string Auth);

/// <summary>F2-02: the signed-in user's devices that accept push notifications.</summary>
internal static class PushEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/push/config", GetConfig)
            .AllowAnonymous()
            .WithName("GetPushConfig");

        group.MapPut("/push/subscriptions", SaveAsync)
            .RequireAuthorization()
            .WithName("SavePushSubscription");

        group.MapDelete("/push/subscriptions", DeleteAsync)
            .RequireAuthorization()
            .WithName("DeletePushSubscription");
    }

    private static Ok<PushConfigDto> GetConfig(IOptions<PushOptions> options) =>
        TypedResults.Ok(new PushConfigDto(options.Value.Enabled ? options.Value.PublicKey : null));

    /// <summary>Idempotent: the same browser (endpoint) is updated, also when another account signed in on it.</summary>
    private static async Task<Results<NoContent, ValidationProblem>> SaveAsync(
        SavePushSubscriptionRequest request, ICurrentUser currentUser, AppDbContext db, CancellationToken ct)
    {
        var valid = Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && request.Endpoint.Length <= WebPushSubscription.EndpointMaxLength
            && !string.IsNullOrWhiteSpace(request.P256dh) && request.P256dh.Length <= WebPushSubscription.KeyMaxLength
            && !string.IsNullOrWhiteSpace(request.Auth) && request.Auth.Length <= WebPushSubscription.KeyMaxLength;
        if (!valid)
        {
            return new ValidationErrors().Add("endpoint", "push.invalid").ToProblem();
        }

        var userId = await currentUser.GetUserIdAsync(ct);
        var existing = await db.Set<WebPushSubscription>().SingleOrDefaultAsync(s => s.Endpoint == request.Endpoint, ct);
        if (existing is null)
        {
            db.Add(WebPushSubscription.Create(userId, request.Endpoint, request.P256dh, request.Auth));
        }
        else
        {
            existing.Update(userId, request.P256dh, request.Auth);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // The same browser registered twice at once: the other request saved it.
        }

        return TypedResults.NoContent();
    }

    private static async Task<NoContent> DeleteAsync(string endpoint, ICurrentUser currentUser, AppDbContext db, CancellationToken ct)
    {
        var userId = await currentUser.GetUserIdAsync(ct);
        await db.Set<WebPushSubscription>().Where(s => s.Endpoint == endpoint && s.UserId == userId).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }
}
