using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Modules.Notifications.Announcements;
using PrenotaConMe.Modules.Notifications.Push;
using PrenotaConMe.Modules.Notifications.Telegram;
using PrenotaConMe.Shared.Modules;
using PrenotaConMe.Shared.Notifications;

namespace PrenotaConMe.Modules.Notifications;

/// <summary>E-mail (F1-11), Web Push (F2-02) and Telegram (F3-T1/T2) notifications, sent by Hangfire jobs (F2-01). WhatsApp and Telegram come in later phases.</summary>
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

        // F3-T1/T2: one Telegram bot per business. The token travels in the URL: no request logging for this client.
        services.Configure<TelegramOptions>(configuration.GetSection("Telegram"));
        services.AddHttpClient<ITelegramApi, HttpTelegramApi>().RemoveAllLoggers();
        services.AddScoped<TelegramService>();
        services.AddScoped<TelegramUpdateHandler>();
        if (configuration.GetSection("Telegram").Get<TelegramOptions>()?.Mode is not TelegramMode.Webhook)
        {
            services.AddHostedService<TelegramPoller>();
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        PushEndpoints.Map(endpoints);
        AnnouncementEndpoints.Map(endpoints);
        TelegramEndpoints.Map(endpoints);
    }
}
