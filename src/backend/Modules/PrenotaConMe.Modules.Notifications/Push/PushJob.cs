using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Notifications.Domain;

namespace PrenotaConMe.Modules.Notifications.Push;

/// <summary>
/// F2-02: fans a notification out to every device of a user, one job per device, so a retry after a push
/// service error never repeats the message on the other devices.
/// </summary>
public sealed partial class PushJob(
    AppDbContext db, IPushSender sender, IBackgroundJobClient jobs, ILogger<PushJob> logger)
{
    public async Task SendToUserAsync(Guid userId, PushNotification notification, CancellationToken cancellationToken)
    {
        var devices = await db.Set<WebPushSubscription>().AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in devices)
        {
            jobs.Enqueue<PushJob>(j => j.SendToDeviceAsync(id, notification, CancellationToken.None));
        }
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task SendToDeviceAsync(Guid subscriptionId, PushNotification notification, CancellationToken cancellationToken)
    {
        var device = await db.Set<WebPushSubscription>().SingleOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken);
        if (device is null)
        {
            return;
        }

        var target = new PushTarget(device.Endpoint, device.P256dh, device.Auth);
        if (await sender.SendAsync(target, notification.ToPayload(), cancellationToken) == PushResult.Gone)
        {
            db.Remove(device);
            await db.SaveChangesAsync(cancellationToken);
            LogGone(logger, subscriptionId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Push subscription {SubscriptionId} expired and was removed")]
    private static partial void LogGone(ILogger logger, Guid subscriptionId);
}
