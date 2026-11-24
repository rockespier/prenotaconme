using System.Net.Mail;
using PrenotaConMe.Shared.Scheduling;
using PrenotaConMe.Shared.Validation;

namespace PrenotaConMe.Modules.Staff;

public static class StaffRules
{
    public const int NameMaxLength = 100;

    /// <summary>Staff availability is defined in 1-hour blocks (original brief).</summary>
    public const int WorkingHoursGranularityMinutes = 60;

    public const int MaxTimeOffDays = 366;

    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValidEmail(string email) =>
        email.Length is > 3 and <= 320 && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email;

    public static ValidationErrors Validate(SaveStaffRequest request)
    {
        var name = request.DisplayName?.Trim() ?? string.Empty;

        return new ValidationErrors()
            .AddIf(name.Length == 0, "displayName", "name.required")
            .AddIf(name.Length > NameMaxLength, "displayName", "name.length")
            .AddIf(!IsValidEmail(NormalizeEmail(request.Email)), "email", "email.invalid");
    }

    /// <summary>
    /// Working hours must be whole hours, must not overlap on the same day (even across locations), must be at
    /// locations assigned to the person and inside that location's opening hours.
    /// </summary>
    public static ValidationErrors ValidateWorkingHours(
        IReadOnlyList<WorkingHoursDto> hours,
        IReadOnlySet<Guid> staffLocationIds,
        IReadOnlyDictionary<Guid, IReadOnlyList<WeeklyInterval>> openingHoursByLocation)
    {
        var errors = new ValidationErrors();
        var intervals = hours.Select(h => new WeeklyInterval(h.Day, h.Start, h.End)).ToList();

        var shapeError = WeeklyIntervals.Validate(intervals, WorkingHoursGranularityMinutes);
        if (shapeError is not null)
        {
            return errors.Add("workingHours", $"workingHours.{shapeError}");
        }

        foreach (var h in hours)
        {
            if (!staffLocationIds.Contains(h.LocationId))
            {
                return errors.Add("workingHours", "workingHours.locationNotAssigned");
            }

            var opening = openingHoursByLocation.GetValueOrDefault(h.LocationId) ?? [];
            if (!WeeklyIntervals.IsCovered(opening, new WeeklyInterval(h.Day, h.Start, h.End)))
            {
                return errors.Add("workingHours", "workingHours.outsideOpeningHours");
            }
        }

        return errors;
    }

    public static ValidationErrors ValidateTimeOff(AddTimeOffRequest request) =>
        new ValidationErrors()
            .AddIf(request.To < request.From, "to", "timeOff.order")
            .AddIf(request.To.DayNumber - request.From.DayNumber >= MaxTimeOffDays, "to", "timeOff.tooLong")
            .AddIf(request.Reason?.Length > 200, "reason", "reason.length");
}
