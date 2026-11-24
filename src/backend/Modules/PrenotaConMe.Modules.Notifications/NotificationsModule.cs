using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Shared.Modules;
using PrenotaConMe.Shared.Notifications;

namespace PrenotaConMe.Modules.Notifications;

/// <summary>E-mail notifications (F1-11), sent by Hangfire jobs (F2-01). Push, WhatsApp and Telegram come in later phases.</summary>
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
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
