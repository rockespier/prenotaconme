using System.Globalization;
using System.Text;

namespace PrenotaConMe.Modules.Notifications;

/// <summary>Minimal RFC 5545 calendar with one event ("add to calendar" attachment).</summary>
internal static class IcsCalendar
{
    public const string ContentType = "text/calendar; method=PUBLISH; charset=utf-8";

    /// <param name="uid">Stable per appointment, so a later update/cancellation can replace the same event.</param>
    public static string Build(
        string uid, DateTimeOffset stamp, DateTimeOffset start, DateTimeOffset end,
        string summary, string? location, string? description)
    {
        var sb = new StringBuilder();
        Line(sb, "BEGIN:VCALENDAR");
        Line(sb, "VERSION:2.0");
        Line(sb, "PRODID:-//PrenotaConMe//Booking//EN");
        Line(sb, "CALSCALE:GREGORIAN");
        Line(sb, "METHOD:PUBLISH");
        Line(sb, "BEGIN:VEVENT");
        Line(sb, "UID:" + Escape(uid));
        Line(sb, "DTSTAMP:" + Utc(stamp));
        Line(sb, "DTSTART:" + Utc(start));
        Line(sb, "DTEND:" + Utc(end));
        Line(sb, "SUMMARY:" + Escape(summary));
        if (!string.IsNullOrWhiteSpace(location))
        {
            Line(sb, "LOCATION:" + Escape(location));
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            Line(sb, "DESCRIPTION:" + Escape(description));
        }

        Line(sb, "STATUS:CONFIRMED");
        Line(sb, "SEQUENCE:0");
        Line(sb, "END:VEVENT");
        Line(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    private static string Utc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    internal static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace(";", "\\;", StringComparison.Ordinal)
        .Replace(",", "\\,", StringComparison.Ordinal)
        .Replace("\r\n", "\\n", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>Appends a content line folded at 75 octets (UTF-8), never splitting a character.</summary>
    private static void Line(StringBuilder sb, string line)
    {
        var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > 75)
            {
                sb.Append("\r\n ");
                octets = 1;
            }

            sb.Append(rune.ToString());
            octets += size;
        }

        sb.Append("\r\n");
    }
}
