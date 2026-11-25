namespace PrenotaConMe.Shared.Notifications;

public enum AppointmentChange
{
    Booked,
    Rescheduled,
    Cancelled,
}

/// <summary>
/// Everything an appointment message needs, captured when the appointment changes, so the
/// notification side never has to query other modules.
/// </summary>
/// <param name="BusinessSlug">For links back to the business's pages.</param>
/// <param name="ClientUserId">Registered client (null for guests entered in the calendar).</param>
/// <param name="StaffUserId">User linked to the staff member (null until they sign in once).</param>
public sealed record AppointmentNotice(
    Guid AppointmentId,
    string BusinessName,
    string BusinessSlug,
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
    Guid? StaffUserId,
    string? ClientName,
    string? ClientEmail,
    Guid? ClientUserId);

/// <summary>Sends appointment messages (implemented by the Notifications module).</summary>
public interface IAppointmentNotifier
{
    /// <summary>
    /// Queues the messages for a change: confirmation e-mails (bookings only) and push notifications to the
    /// client and the staff member, except to <paramref name="actorUserId"/>, who made the change.
    /// Returns immediately and never throws: a failed message must not undo or delay the change.
    /// </summary>
    void Notify(AppointmentChange change, AppointmentNotice notice, Guid? actorUserId);

    /// <summary>
    /// F2-06: queues a reminder of an upcoming appointment to the registered client (push, Telegram, e-mail), unless
    /// they turned reminders off for this business. Guests (no account) get nothing.
    /// </summary>
    Task RemindAsync(AppointmentNotice notice, CancellationToken cancellationToken);
}
