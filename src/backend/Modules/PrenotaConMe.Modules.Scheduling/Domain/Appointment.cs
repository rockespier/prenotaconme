using PrenotaConMe.Shared.Domain;

namespace PrenotaConMe.Modules.Scheduling.Domain;

public enum AppointmentStatus
{
    Confirmed,
    Cancelled,
    Completed,
    NoShow,
}

/// <summary>
/// A booked service with one staff member at one location. Times are UTC instants; the database
/// forbids two non-cancelled appointments of the same staff member from overlapping (EXCLUDE constraint).
/// </summary>
public sealed class Appointment : TenantEntity
{
    private Appointment()
    {
    }

    public Guid LocationId { get; private set; }

    public Guid ServiceId { get; private set; }

    public Guid StaffMemberId { get; private set; }

    /// <summary>Local user id of the client.</summary>
    public Guid ClientUserId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public AppointmentStatus Status { get; private set; } = AppointmentStatus.Confirmed;

    /// <summary>Service name and price when booked; later catalog changes do not alter past appointments.</summary>
    public string ServiceName { get; private set; } = string.Empty;

    public decimal Price { get; private set; }

    public static Appointment Book(
        Guid locationId, Guid serviceId, string serviceName, decimal price, Guid staffMemberId, Guid clientUserId,
        DateTimeOffset startsAt, int durationMinutes) => new()
    {
        LocationId = locationId,
        ServiceId = serviceId,
        ServiceName = serviceName,
        Price = price,
        StaffMemberId = staffMemberId,
        ClientUserId = clientUserId,
        StartsAt = startsAt.ToUniversalTime(),
        EndsAt = startsAt.ToUniversalTime().AddMinutes(durationMinutes),
    };
}
