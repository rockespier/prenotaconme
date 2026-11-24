using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Staff.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Modules;

namespace PrenotaConMe.Modules.Staff;

/// <summary>Staff members, their locations/services, weekly working hours and time off (F1-06, F1-07).</summary>
public sealed class StaffModule : IModule
{
    public string Name => "staff";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddScoped<IUserSignInHook, StaffInvitationLinker>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => StaffEndpoints.Map(endpoints);
}

/// <summary>
/// On sign-in, links pending staff records (any business) created with the user's verified e-mail
/// and grants the Staff role there. This is how invitations are accepted.
/// </summary>
internal sealed class StaffInvitationLinker(AppDbContext db, IMembershipWriter memberships) : IUserSignInHook
{
    public async Task OnSignedInAsync(Guid userId, string? verifiedEmail, CancellationToken cancellationToken)
    {
        if (verifiedEmail is null)
        {
            return;
        }

        var pending = await db.Set<StaffMember>()
            .IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(s => s.Email == verifiedEmail && s.UserId == null)
            .ToListAsync(cancellationToken);

        foreach (var staff in pending)
        {
            staff.LinkUser(userId);
            await memberships.EnsureAsync(staff.TenantId, userId, TenantRole.Staff, cancellationToken);
        }
    }
}
