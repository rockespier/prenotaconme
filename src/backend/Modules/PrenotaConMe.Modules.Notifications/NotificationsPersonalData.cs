using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Notifications.Domain;
using PrenotaConMe.Shared.Privacy;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Notifications;

/// <summary>F5-04: devices, Telegram links and reminder choices (all deleted with the account by cascade).</summary>
internal sealed class NotificationsPersonalData(AppDbContext db, ITenantDirectory tenants) : IPersonalData
{
    public string Section => "notifications";

    public async Task<object?> ExportAsync(Guid userId, CancellationToken cancellationToken)
    {
        var devices = await db.Set<WebPushSubscription>().AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
        var telegram = await db.Set<TelegramLink>().IgnoreQueryFilters([AppDbContext.TenantFilter]).AsNoTracking()
            .Where(l => l.UserId == userId)
            .Select(l => new { l.TenantId, l.CreatedAt })
            .ToListAsync(cancellationToken);
        var reminders = await db.Set<ReminderPreference>().IgnoreQueryFilters([AppDbContext.TenantFilter]).AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.TenantId, p.Enabled })
            .ToListAsync(cancellationToken);
        if (devices.Count == 0 && telegram.Count == 0 && reminders.Count == 0)
        {
            return null;
        }

        var ids = telegram.Select(t => t.TenantId).Concat(reminders.Select(r => r.TenantId)).Distinct().ToList();
        var businesses = await tenants.GetByIdsAsync(ids, cancellationToken);
        string? Name(Guid id) => businesses.TryGetValue(id, out var b) ? b.Name : null;

        return new
        {
            PushDevicesRegisteredAt = devices,
            TelegramLinkedTo = telegram.Select(t => new { Business = Name(t.TenantId), LinkedAt = t.CreatedAt }),
            Reminders = reminders.Select(r => new { Business = Name(r.TenantId), r.Enabled }),
        };
    }

    public Task EraseAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
}
