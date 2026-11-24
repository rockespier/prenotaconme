using PrenotaConMe.Shared.Scheduling;
using PrenotaConMe.Shared.Validation;

namespace PrenotaConMe.Modules.Locations;

public static class LocationRules
{
    public const int NameMaxLength = 100;

    /// <summary>Opening hours use 30-minute steps (services are multiples of 30 minutes).</summary>
    public const int OpeningHoursGranularityMinutes = 30;

    public static ValidationErrors Validate(SaveLocationRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var hours = (request.OpeningHours ?? []).Select(h => new WeeklyInterval(h.Day, h.Opens, h.Closes)).ToList();
        var hoursError = WeeklyIntervals.Validate(hours, OpeningHoursGranularityMinutes);

        return new ValidationErrors()
            .AddIf(name.Length == 0, "name", "name.required")
            .AddIf(name.Length > NameMaxLength, "name", "name.length")
            .AddIf(request.Address?.Length > 200, "address", "address.length")
            .AddIf(request.City?.Length > 100, "city", "city.length")
            .AddIf(request.Phone?.Length > 32, "phone", "phone.length")
            .AddIf(!TimeZones.IsValidIana(request.TimeZone), "timeZone", "timeZone.invalid")
            .AddIf(hoursError is not null, "openingHours", $"openingHours.{hoursError}");
    }
}
