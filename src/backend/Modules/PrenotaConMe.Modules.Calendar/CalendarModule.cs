using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PrenotaConMe.Shared.Calendar;
using PrenotaConMe.Shared.Modules;

namespace PrenotaConMe.Modules.Calendar;

/// <summary>
/// F2-07..09: optional Google Calendar sync, turned on per business. Each staff member connects their own calendar;
/// appointments are exported to it and its busy times block free slots (FreeBusy). Depends on Scheduling and
/// replaces its <see cref="NoExternalCalendar"/> defaults.
/// </summary>
public sealed class CalendarModule : IModule
{
    public string Name => "calendar";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GoogleOptions>(configuration.GetSection("Google"));
        services.AddHttpClient<IGoogleCalendarApi, HttpGoogleCalendarApi>((http, sp) =>
            new HttpGoogleCalendarApi(http, sp.GetRequiredService<IOptions<GoogleOptions>>().Value));
        services.AddScoped<GoogleTokens>();
        services.AddScoped<IExternalBusyTimes, GoogleBusyTimes>();
        services.AddScoped<IAppointmentCalendarSync, GoogleAppointmentSync>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => GoogleCalendarEndpoints.Map(endpoints);
}
