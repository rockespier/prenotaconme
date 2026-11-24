namespace PrenotaConMe.Shared.Scheduling;

public static class TimeZones
{
    /// <summary>Only IANA ids (e.g. "Europe/Rome"), valid on both Linux and Windows.</summary>
    public static bool IsValidIana(string? timeZone) =>
        !string.IsNullOrWhiteSpace(timeZone)
        && TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZone, out _)
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _);
}
