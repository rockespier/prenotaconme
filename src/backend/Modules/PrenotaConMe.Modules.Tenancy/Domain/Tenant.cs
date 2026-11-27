using PrenotaConMe.Shared.Domain;

namespace PrenotaConMe.Modules.Tenancy.Domain;

/// <summary>A business using the platform. Not tenant-owned itself: it *is* the tenant.</summary>
public sealed class Tenant : Entity
{
    private Tenant()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Unique, URL-safe identifier used in the public booking page (/{slug}) and the X-Tenant header.</summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>IANA time zone (e.g. "Europe/Rome"); default for locations of this business.</summary>
    public string TimeZone { get; private set; } = string.Empty;

    public string DefaultLanguage { get; private set; } = "it";

    public bool IsActive { get; private set; } = true;

    /// <summary>Minimum notice (hours before the start) for a client to cancel or reschedule an appointment.</summary>
    public int CancellationNoticeHours { get; private set; } = TenantRules.DefaultCancellationNoticeHours;

    public void SetCancellationNotice(int hours) => CancellationNoticeHours = hours;

    /// <summary>F2-06: first reminder to the client, hours before the start (0 = off).</summary>
    public int FirstReminderHours { get; private set; } = TenantRules.DefaultFirstReminderHours;

    /// <summary>F2-06: second reminder, hours before the start (0 = off).</summary>
    public int SecondReminderHours { get; private set; } = TenantRules.DefaultSecondReminderHours;

    public void SetReminders(int firstHours, int secondHours)
    {
        FirstReminderHours = firstHours;
        SecondReminderHours = secondHours;
    }

    /// <summary>
    /// F2-07: the business lets its staff connect Google Calendar (appointments exported, personal events block
    /// free times). Off by default: the backoffice calendar is enough for many businesses.
    /// </summary>
    public bool GoogleCalendarSync { get; private set; }

    public void SetGoogleCalendarSync(bool enabled) => GoogleCalendarSync = enabled;

    /// <summary>F5-08: version (Unix ms of the upload) of the logo, null without one. Part of the image URL.</summary>
    public long? LogoVersion { get; private set; }

    /// <summary>F5-08: version of the cover image, null without one.</summary>
    public long? CoverVersion { get; private set; }

    /// <summary>The icon goes with the logo, so it uses the logo's version.</summary>
    public long? ImageVersion(TenantImageKind kind) => kind == TenantImageKind.Cover ? CoverVersion : LogoVersion;

    public void SetImageVersion(TenantImageKind kind, long? version)
    {
        if (kind != TenantImageKind.Cover)
        {
            LogoVersion = version;
        }
        else
        {
            CoverVersion = version;
        }
    }

    /// <summary>Inputs must already be validated with <see cref="TenantRules"/>.</summary>
    public static Tenant Create(string name, string normalizedSlug, string timeZone, string language) => new()
    {
        Name = name.Trim(),
        Slug = normalizedSlug,
        TimeZone = timeZone,
        DefaultLanguage = language,
    };
}
