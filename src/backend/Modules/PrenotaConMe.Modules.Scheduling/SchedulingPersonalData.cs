using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Shared.Privacy;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Scheduling;

/// <summary>F5-04: the user's appointments in every business. Appointments have no foreign key to the user.</summary>
internal sealed class SchedulingPersonalData(AppDbContext db, ITenantDirectory tenants, TimeProvider clock) : IPersonalData
{
    public string Section => "appointments";

    public async Task<object?> ExportAsync(Guid userId, CancellationToken cancellationToken)
    {
        var appointments = await Mine(userId).AsNoTracking()
            .OrderBy(a => a.StartsAt)
            .Select(a => new { a.TenantId, a.StartsAt, a.EndsAt, a.ServiceName, a.Price, a.ChargedAmount, a.Status, a.CancelledAt, a.CreatedAt })
            .ToListAsync(cancellationToken);
        if (appointments.Count == 0)
        {
            return null;
        }

        var businesses = await tenants.GetByIdsAsync(appointments.Select(a => a.TenantId).Distinct().ToList(), cancellationToken);
        return appointments.Select(a => new
        {
            Business = businesses.TryGetValue(a.TenantId, out var b) ? b.Name : null,
            a.StartsAt,
            a.EndsAt,
            Service = a.ServiceName,
            a.Price,
            a.ChargedAmount,
            Status = a.Status.ToString(),
            a.CancelledAt,
            BookedAt = a.CreatedAt,
        }).ToList();
    }

    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var appointment in await Mine(userId).ToListAsync(cancellationToken))
        {
            appointment.EraseClient(now);
        }
    }

    private IQueryable<Appointment> Mine(Guid userId) =>
        db.Set<Appointment>().IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(a => a.ClientUserId == userId);
}
