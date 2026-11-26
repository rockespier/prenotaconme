namespace PrenotaConMe.Shared.Calendar;

/// <summary>A busy interval in a staff member's external calendar (UTC instants, end exclusive).</summary>
public readonly record struct BusyInterval(DateTimeOffset Start, DateTimeOffset End);

/// <summary>
/// F2-09: busy times of staff members in their own calendars (implemented by the Calendar module with Google
/// FreeBusy). Never throws: when the calendar cannot be read the staff member simply has no external busy times.
/// </summary>
public interface IExternalBusyTimes
{
    /// <returns>Only the staff members with a connected calendar in the current business.</returns>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<BusyInterval>>> GetAsync(
        IReadOnlyCollection<Guid> staffMemberIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>
/// F2-08: keeps the staff member's external calendar in step with an appointment (implemented by the Calendar
/// module). Returns at once and never throws: the appointment change is already committed.
/// </summary>
public interface IAppointmentCalendarSync
{
    /// <summary>The appointment was booked, moved (maybe to another staff member) or cancelled.</summary>
    void Changed(Guid appointmentId);
}

/// <summary>Defaults while no calendar module is registered: nothing busy, nothing to sync.</summary>
public sealed class NoExternalCalendar : IExternalBusyTimes, IAppointmentCalendarSync
{
    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<BusyInterval>>> GetAsync(
        IReadOnlyCollection<Guid> staffMemberIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<BusyInterval>>>(new Dictionary<Guid, IReadOnlyList<BusyInterval>>());

    public void Changed(Guid appointmentId)
    {
    }
}
