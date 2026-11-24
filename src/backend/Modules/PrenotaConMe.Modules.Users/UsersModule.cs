using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Modules;

namespace PrenotaConMe.Modules.Users;

/// <summary>Users (Firebase identities), tenant memberships and role-based authorization (F1-02, F1-03).</summary>
public sealed class UsersModule : IModule
{
    public string Name => "users";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IMembershipWriter, MembershipWriter>();
        services.AddScoped<IAuthorizationHandler, TenantRoleHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => UsersEndpoints.Map(endpoints);
}
