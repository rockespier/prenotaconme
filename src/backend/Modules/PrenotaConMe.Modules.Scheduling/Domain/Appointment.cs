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

    public const int ClientNameMaxLength = 100;
    public const int ClientPhoneMaxLength = 32;

    /// <summary>Local user id of the client; null when the business entered the appointment for a client without an account.</summary>
    public Guid? ClientUserId { get; private set; }

    /// <summary>Client entered by the business (phone or walk-in booking), when there is no <see cref="ClientUserId"/>.</summary>
    public string? ClientName { get; private set; }

    public string? ClientPhone { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public AppointmentStatus Status { get; private set; } = AppointmentStatus.Confirmed;

    /// <summary>Service name and price when booked; later catalog changes do not alter past appointments.</summary>
    public string ServiceName { get; private set; } = string.Empty;

    public decimal Price { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>
    /// F4-01: what the client actually paid, when it differs from <see cref="Price"/> (surcharge, discount).
    /// Only for <see cref="AppointmentStatus.Completed"/>.
    /// </summary>
    public decimal? ChargedAmount { get; private set; }

    /// <summary>
    /// F4-01: the business records how a started appointment went: completed (optionally with the amount charged),
    /// no-show, or back to confirmed (undo).
    /// </summary>
    public void RecordOutcome(AppointmentStatus outcome, decimal? chargedAmount)
    {
        if (outcome is not (AppointmentStatus.Completed or AppointmentStatus.NoShow or AppointmentStatus.Confirmed))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        Status = outcome;
        ChargedAmount = outcome == AppointmentStatus.Completed && chargedAmount != Price ? chargedAmount : null;
    }

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

    /// <summary>Created from the backoffice for a client without an account (phone or walk-in).</summary>
    public static Appointment BookForGuest(
        Guid locationId, Guid serviceId, string serviceName, decimal price, Guid staffMemberId,
        string clientName, string? clientPhone, DateTimeOffset startsAt, int durationMinutes)
    {
        var appointment = Book(locationId, serviceId, serviceName, price, staffMemberId, Guid.Empty, startsAt, durationMinutes);
        appointment.ClientUserId = null;
        appointment.ClientName = clientName.Trim();
        appointment.ClientPhone = string.IsNullOrWhiteSpace(clientPhone) ? null : clientPhone.Trim();
        return appointment;
    }

    /// <summary>
    /// Latest instant at which the client may still cancel or reschedule, given the business notice.
    /// </summary>
    public DateTimeOffset ChangeableUntil(int noticeHours) => StartsAt.AddHours(-noticeHours);

    /// <summary>The client may cancel or reschedule: still confirmed and the notice deadline has not passed.</summary>
    public bool CanChange(DateTimeOffset now, int noticeHours) =>
        Status == AppointmentStatus.Confirmed && now <= ChangeableUntil(noticeHours);

    public void Cancel(DateTimeOffset now)
    {
        Status = AppointmentStatus.Cancelled;
        CancelledAt = now;
    }

    /// <summary>Name left on appointments whose client asked to be erased (F5-04); keeps ck_appointments_has_client.</summary>
    public const string ErasedClientName = "—";

    /// <summary>
    /// F5-04 (GDPR erasure): removes who the client was, keeping the appointment for the business's history and
    /// finance. A future confirmed appointment is cancelled, so its time becomes free again.
    /// </summary>
    public void EraseClient(DateTimeOffset now)
    {
        if (Status == AppointmentStatus.Confirmed && StartsAt > now)
        {
            Cancel(now);
        }

        ClientUserId = null;
        ClientName = ErasedClientName;
        ClientPhone = null;
    }

    /// <summary>Moves the appointment to another (already validated) slot, keeping service, price and location.</summary>
    public void Reschedule(Guid staffMemberId, DateTimeOffset startsAt)
    {
        var duration = EndsAt - StartsAt;
        StaffMemberId = staffMemberId;
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = StartsAt + duration;
    }
}
