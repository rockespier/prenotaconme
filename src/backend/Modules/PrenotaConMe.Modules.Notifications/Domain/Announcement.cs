using PrenotaConMe.Shared.Domain;
using PrenotaConMe.Shared.Validation;

namespace PrenotaConMe.Modules.Notifications.Domain;

/// <summary>
/// F2-03: a message from the business to its clients (e.g. holiday closures, informative surcharges), shown on
/// the public pages between <see cref="StartsOn"/> and <see cref="EndsOn"/> (business-local dates, inclusive)
/// and optionally sent once by push and e-mail.
/// </summary>
public sealed class Announcement : TenantEntity
{
    public const int TitleMaxLength = 100;
    public const int MessageMaxLength = 1000;
    public const int MaxDays = 366;

    private Announcement()
    {
    }

    public string Title { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public DateOnly StartsOn { get; private set; }

    public DateOnly EndsOn { get; private set; }

    /// <summary>When it was queued for sending to the clients (null = only shown on the pages).</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Clients it was sent to.</summary>
    public int? RecipientCount { get; private set; }

    public static Announcement Create(string title, string message, DateOnly startsOn, DateOnly endsOn)
    {
        var announcement = new Announcement();
        announcement.Update(title, message, startsOn, endsOn);
        return announcement;
    }

    public void Update(string title, string message, DateOnly startsOn, DateOnly endsOn)
    {
        Title = title.Trim();
        Message = message.Trim();
        StartsOn = startsOn;
        EndsOn = endsOn;
    }

    public void MarkSent(DateTimeOffset now, int recipients)
    {
        SentAt = now;
        RecipientCount = recipients;
    }

    public void ClearSent()
    {
        SentAt = null;
        RecipientCount = null;
    }

    public bool IsShownOn(DateOnly day) => StartsOn <= day && day <= EndsOn;

    public static ValidationErrors Validate(string? title, string? message, DateOnly startsOn, DateOnly endsOn)
    {
        var t = title?.Trim() ?? string.Empty;
        var m = message?.Trim() ?? string.Empty;
        return new ValidationErrors()
            .AddIf(t.Length == 0, "title", "announcement.title.required")
            .AddIf(t.Length > TitleMaxLength, "title", "announcement.title.length")
            .AddIf(m.Length == 0, "message", "announcement.message.required")
            .AddIf(m.Length > MessageMaxLength, "message", "announcement.message.length")
            .AddIf(endsOn < startsOn || endsOn.DayNumber - startsOn.DayNumber >= MaxDays, "endsOn", "announcement.dates.invalid");
    }
}
