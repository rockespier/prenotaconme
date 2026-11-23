using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Shared.Modules;

namespace PrenotaConMe.Api.Hosting;

internal static class ModuleExtensions
{
    public static IServiceCollection AddModules(
        this IServiceCollection services, IConfiguration configuration, IEnumerable<IModule> modules)
    {
        foreach (var module in modules)
        {
            services.AddSingleton(new ModelAssembly(module.GetType().Assembly));
            module.RegisterServices(services, configuration);
        }

        return services;
    }

    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder api, IEnumerable<IModule> modules)
    {
        foreach (var module in modules)
        {
            var group = api.MapGroup($"/{module.Name}").WithTags(module.Name);
            module.MapEndpoints(group);
        }

        return api;
    }
}
