namespace PrenotaConMe.Shared.Notifications;

/// <summary>
/// Everything a confirmation message needs, captured when the appointment is booked, so the
/// notification side never has to query other modules.
/// </summary>
public sealed record AppointmentNotice(
    Guid AppointmentId,
    string BusinessName,
    string Language,
    string ServiceName,
    decimal Price,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string TimeZone,
    string LocationName,
    string? LocationAddress,
    string? LocationCity,
    string? LocationPhone,
    string StaffName,
    string? StaffEmail,
    string? ClientName,
    string? ClientEmail);

/// <summary>Sends appointment messages (implemented by the Notifications module).</summary>
public interface IAppointmentNotifier
{
    /// <summary>
    /// Queues the confirmation to the client and the staff member. Returns immediately and never throws:
    /// a failed message must not undo or delay a booking.
    /// </summary>
    void Booked(AppointmentNotice notice);
}
