using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Catalog.Domain;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Shared.Modules;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Catalog;

/// <summary>Bookable services: duration, price, locations (F1-05).</summary>
public sealed class CatalogModule : IModule
{
    public string Name => "catalog";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddScoped<ITenantInitializer, SampleServicesInitializer>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => CatalogEndpoints.Map(endpoints);
}

/// <summary>Seeds the three services from the original brief at the default location (runs after it is staged).</summary>
internal sealed class SampleServicesInitializer(AppDbContext db) : ITenantInitializer
{
    public int Order => 20;

    public Task InitializeAsync(TenantInitializationContext context, CancellationToken cancellationToken)
    {
        var locationIds = db.Set<Location>().Local.Where(l => l.TenantId == context.TenantId).Select(l => l.Id).ToList();

        (string Name, int Minutes, decimal Price)[] samples = context.Language switch
        {
            "es" => [("Corte de pelo", 30, 20m), ("Arreglo de barba", 30, 15m), ("Corte + barba", 60, 30m)],
            "en" => [("Haircut", 30, 20m), ("Beard trim", 30, 15m), ("Haircut + beard", 60, 30m)],
            _ => [("Taglio capelli", 30, 20m), ("Regolazione barba", 30, 15m), ("Taglio + barba", 60, 30m)],
        };

        foreach (var (name, minutes, price) in samples)
        {
            var service = Service.Create(new ServiceData(name, null, minutes, price, locationIds.Count > 0, locationIds));
            service.TenantId = context.TenantId;
            foreach (var link in service.Locations)
            {
                link.TenantId = context.TenantId;
            }

            db.Add(service);
        }

        return Task.CompletedTask;
    }
}
