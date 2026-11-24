using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Shared.Modules;

namespace PrenotaConMe.Modules.Scheduling;

/// <summary>Availability (F1-08) and appointments (F1-09). Depends on Staff, Catalog and Locations.</summary>
public sealed class SchedulingModule : IModule
{
    public string Name => "scheduling";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AvailabilityService>();
        services.AddScoped<AppointmentEvents>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => SchedulingEndpoints.Map(endpoints);
}
