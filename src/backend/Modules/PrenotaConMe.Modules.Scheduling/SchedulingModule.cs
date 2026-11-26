using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PrenotaConMe.Shared.Calendar;
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
        services.AddScoped<ClientBookingService>();
        services.AddScoped<CalendarBroadcast>();
        services.AddSignalR();

        // Replaced by the Calendar module (F2-07..09) when it is registered.
        services.TryAddScoped<IExternalBusyTimes, NoExternalCalendar>();
        services.TryAddScoped<IAppointmentCalendarSync, NoExternalCalendar>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => SchedulingEndpoints.Map(endpoints);
}
