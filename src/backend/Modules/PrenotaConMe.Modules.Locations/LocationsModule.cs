using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Shared.Modules;
using PrenotaConMe.Shared.Scheduling;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Locations;

/// <summary>Business locations with address, time zone and opening hours (F1-04).</summary>
public sealed class LocationsModule : IModule
{
    public string Name => "locations";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddScoped<ITenantInitializer, DefaultLocationInitializer>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => LocationsEndpoints.Map(endpoints);
}

/// <summary>Every new business starts with one location open Tuesday–Saturday 09:00–19:00 (editable).</summary>
internal sealed class DefaultLocationInitializer(AppDbContext db) : ITenantInitializer
{
    public const int InitializerOrder = 10;

    public int Order => InitializerOrder;

    public Task InitializeAsync(TenantInitializationContext context, CancellationToken cancellationToken)
    {
        var name = context.Language switch
        {
            "es" => "Sede principal",
            "en" => "Main location",
            _ => "Sede principale",
        };

        DayOfWeek[] days = [DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];
        var hours = days.Select(d => new WeeklyInterval(d, new TimeOnly(9, 0), new TimeOnly(19, 0))).ToList();

        var location = Location.Create(new LocationData(name, null, null, null, context.TimeZone, true, hours));
        location.TenantId = context.TenantId;
        db.Add(location);

        return Task.CompletedTask;
    }
}
