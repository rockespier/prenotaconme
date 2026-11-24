using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Shared.Modules;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Tenancy;

/// <summary>Businesses (tenants): registration, public profile, slug resolution (F1-01).</summary>
public sealed class TenancyModule : IModule
{
    public string Name => "tenancy";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddScoped<ITenantDirectory, TenantDirectory>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => TenancyEndpoints.Map(endpoints);
}
