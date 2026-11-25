using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Notifications.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Notifications.Reminders;

public sealed record ReminderPreferenceDto(bool Enabled);

/// <summary>F2-06: the signed-in client turns appointment reminders from this business on or off.</summary>
internal static class ReminderEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/reminders/preference", GetAsync)
            .RequireTenant()
            .RequireAuthorization()
            .WithName("GetReminderPreference");

        group.MapPut("/reminders/preference", SaveAsync)
            .RequireTenant()
            .RequireAuthorization()
            .WithName("SaveReminderPreference");
    }

    private static async Task<Ok<ReminderPreferenceDto>> GetAsync(AppDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var userId = await currentUser.GetUserIdAsync(ct);
        return TypedResults.Ok(new ReminderPreferenceDto(await IsEnabledAsync(db, userId, ct)));
    }

    private static async Task<Ok<ReminderPreferenceDto>> SaveAsync(
        ReminderPreferenceDto request, AppDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var userId = await currentUser.GetUserIdAsync(ct);
        var preference = await db.Set<ReminderPreference>().SingleOrDefaultAsync(p => p.UserId == userId, ct);
        if (preference is null)
        {
            db.Add(ReminderPreference.Create(userId, request.Enabled));
        }
        else
        {
            preference.Set(request.Enabled);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new ReminderPreferenceDto(request.Enabled));
    }

    /// <summary>On unless the user turned reminders off in the current tenant.</summary>
    public static async Task<bool> IsEnabledAsync(AppDbContext db, Guid userId, CancellationToken ct) =>
        await db.Set<ReminderPreference>().Where(p => p.UserId == userId).Select(p => (bool?)p.Enabled).SingleOrDefaultAsync(ct) ?? true;
}
