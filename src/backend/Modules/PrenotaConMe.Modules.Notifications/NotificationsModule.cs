using System.Threading.Channels;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrenotaConMe.Shared.Modules;
using PrenotaConMe.Shared.Notifications;

namespace PrenotaConMe.Modules.Notifications;

/// <summary>E-mail notifications (F1-11). Push, WhatsApp and Telegram come in later phases.</summary>
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

        services.AddSingleton<NotificationQueue>();
        services.AddSingleton<IAppointmentNotifier, AppointmentNotifier>();
        services.AddHostedService<AppointmentEmailWorker>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}

/// <summary>
/// In-memory queue so the booking request does not wait for SMTP. Messages still queued when the app stops are
/// lost; durable jobs with retries arrive with Hangfire (F2-01).
/// </summary>
internal sealed class NotificationQueue
{
    private readonly Channel<AppointmentNotice> _channel =
        Channel.CreateUnbounded<AppointmentNotice>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<AppointmentNotice> Reader => _channel.Reader;

    public bool TryWrite(AppointmentNotice notice) => _channel.Writer.TryWrite(notice);
}

internal sealed class AppointmentNotifier(NotificationQueue queue) : IAppointmentNotifier
{
    public void Booked(AppointmentNotice notice) => queue.TryWrite(notice);
}

internal sealed partial class AppointmentEmailWorker(
    NotificationQueue queue, IEmailSender sender, IOptions<NotificationOptions> options, TimeProvider clock,
    ILogger<AppointmentEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var notice in queue.Reader.ReadAllAsync(stoppingToken))
        {
            IReadOnlyList<EmailMessage> messages;
            try
            {
                messages = AppointmentEmails.Build(notice, options.Value.StaffEmail, clock.GetUtcNow());
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or FormatException)
            {
                LogBuildFailed(logger, ex, notice.AppointmentId);
                continue;
            }

            foreach (var message in messages)
            {
                try
                {
                    await sender.SendAsync(message, stoppingToken);
                    LogSent(logger, notice.AppointmentId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Never let one bad address or an SMTP outage stop the worker.
                    LogSendFailed(logger, ex, notice.AppointmentId);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Confirmation e-mail sent for appointment {AppointmentId}")]
    private static partial void LogSent(ILogger logger, Guid appointmentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Confirmation e-mail failed for appointment {AppointmentId}")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, Guid appointmentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not build the confirmation e-mails for appointment {AppointmentId}")]
    private static partial void LogBuildFailed(ILogger logger, Exception exception, Guid appointmentId);
}
