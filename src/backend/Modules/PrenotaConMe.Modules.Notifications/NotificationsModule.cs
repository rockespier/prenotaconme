using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Modules.Notifications.Push;
using PrenotaConMe.Shared.Modules;
using PrenotaConMe.Shared.Notifications;

namespace PrenotaConMe.Modules.Notifications;

/// <summary>E-mail (F1-11) and Web Push (F2-02) notifications, sent by Hangfire jobs (F2-01). WhatsApp and Telegram come in later phases.</summary>
public sealed class NotificationsModule : IModule
{
    public string Name => "notifications";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SmtpOptions>(configuration.GetSection("Smtp"));
        services.Configure<NotificationOptions>(configuration.GetSection("Notifications"));

        if (string.IsNullOrWhiteSpace(configuration["Smtp:Host"]))
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }

        services.AddScoped<IAppointmentNotifier, AppointmentNotifier>();

        // F2-02: Web Push (VAPID). Without keys, push is off and /push/config returns no public key.
        services.Configure<PushOptions>(configuration.GetSection("Push"));
        services.PostConfigure<PushOptions>(o => o.Subject ??= string.IsNullOrWhiteSpace(configuration["Smtp:From"])
            ? "mailto:noreply@prenotaconme.app"
            : "mailto:" + configuration["Smtp:From"]);
        services.AddHttpClient<IPushSender, VapidPushSender>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => PushEndpoints.Map(endpoints);
}
