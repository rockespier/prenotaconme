using PrenotaConMe.Shared.Domain;
using PrenotaConMe.Shared.Scheduling;

namespace PrenotaConMe.Modules.Locations.Domain;

/// <summary>A physical place where the business operates. Staff and services are assigned to locations.</summary>
public sealed class Location : TenantEntity
{
    private Location()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string? Address { get; private set; }

    public string? City { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>IANA time zone; all hours of this location are local to it.</summary>
    public string TimeZone { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    /// <summary>Weekly opening hours (stored as JSON). Staff working hours must fall within them.</summary>
    public List<OpeningHours> OpeningHours { get; private set; } = [];

    public IEnumerable<WeeklyInterval> OpeningIntervals => OpeningHours.Select(h => new WeeklyInterval(h.Day, h.Opens, h.Closes));

    public static Location Create(LocationData data)
    {
        var location = new Location();
        location.Update(data);
        return location;
    }

    public void Update(LocationData data)
    {
        Name = data.Name.Trim();
        Address = Clean(data.Address);
        City = Clean(data.City);
        Phone = Clean(data.Phone);
        TimeZone = data.TimeZone;
        IsActive = data.IsActive;
        OpeningHours = data.OpeningHours
            .OrderBy(h => ((int)h.Day + 6) % 7) // Monday first
            .ThenBy(h => h.Start)
            .Select(h => new OpeningHours { Day = h.Day, Opens = h.Start, Closes = h.End })
            .ToList();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class OpeningHours
{
    public DayOfWeek Day { get; set; }

    public TimeOnly Opens { get; set; }

    public TimeOnly Closes { get; set; }
}

public sealed record LocationData(
    string Name, string? Address, string? City, string? Phone, string TimeZone, bool IsActive, IReadOnlyList<WeeklyInterval> OpeningHours);
