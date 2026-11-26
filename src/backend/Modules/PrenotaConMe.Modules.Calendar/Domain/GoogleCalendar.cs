using PrenotaConMe.Shared.Domain;

namespace PrenotaConMe.Modules.Calendar.Domain;

/// <summary>
/// F2-07: a staff member's Google Calendar, connected by the staff member themselves (OAuth, offline access).
/// One per staff member; the refresh token is encrypted with Data Protection.
/// </summary>
public sealed class GoogleCalendarConnection : TenantEntity
{
    public const int EmailMaxLength = 320;
    public const int CalendarIdMaxLength = 200;
    public const int ErrorMaxLength = 100;

    private GoogleCalendarConnection()
    {
    }

    public Guid StaffMemberId { get; private set; }

    /// <summary>Local user who granted access.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The Google account, shown to the staff member.</summary>
    public string? GoogleEmail { get; private set; }

    public string ProtectedRefreshToken { get; private set; } = string.Empty;

    /// <summary>Calendar the appointments go to and whose busy times count ("primary").</summary>
    public string CalendarId { get; private set; } = "primary";

    /// <summary>Why Google stopped accepting the connection (e.g. "revoked"); the staff member must connect again.</summary>
    public string? Error { get; private set; }

    public static GoogleCalendarConnection Create(Guid staffMemberId, Guid userId, string? googleEmail, string protectedRefreshToken) =>
        new() { StaffMemberId = staffMemberId, UserId = userId, GoogleEmail = googleEmail, ProtectedRefreshToken = protectedRefreshToken };

    public void Reconnect(Guid userId, string? googleEmail, string protectedRefreshToken)
    {
        UserId = userId;
        GoogleEmail = googleEmail;
        ProtectedRefreshToken = protectedRefreshToken;
        Error = null;
    }

    public void Fail(string error) => Error = error.Length > ErrorMaxLength ? error[..ErrorMaxLength] : error;
}

/// <summary>F2-08: the Google event created for an appointment, in the calendar of <see cref="StaffMemberId"/>.</summary>
public sealed class CalendarEventLink : TenantEntity
{
    public const int EventIdMaxLength = 1024;

    private CalendarEventLink()
    {
    }

    public Guid AppointmentId { get; private set; }

    public Guid StaffMemberId { get; private set; }

    public string EventId { get; private set; } = string.Empty;

    public static CalendarEventLink Create(Guid appointmentId, Guid staffMemberId, string eventId) =>
        new() { AppointmentId = appointmentId, StaffMemberId = staffMemberId, EventId = eventId };

    public void Replace(string eventId) => EventId = eventId;
}
