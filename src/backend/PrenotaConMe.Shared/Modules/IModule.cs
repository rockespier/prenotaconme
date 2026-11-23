using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PrenotaConMe.Shared.Modules;

/// <summary>
/// Contract each business module (Catalog, Staff, Scheduling, ...) implements.
/// The API host discovers modules and calls these at startup.
/// </summary>
public interface IModule
{
    /// <summary>Route prefix under /api, e.g. "catalog".</summary>
    string Name { get; }

    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
