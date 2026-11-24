using PrenotaConMe.Shared.Validation;

namespace PrenotaConMe.Modules.Catalog;

public static class ServiceRules
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;
    public const int SlotMinutes = 30;
    public const int MaxDurationMinutes = 480;
    public const decimal MaxPrice = 100_000m;

    public static ValidationErrors Validate(SaveServiceRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var locations = request.LocationIds ?? [];

        return new ValidationErrors()
            .AddIf(name.Length == 0, "name", "name.required")
            .AddIf(name.Length > NameMaxLength, "name", "name.length")
            .AddIf(request.Description?.Length > DescriptionMaxLength, "description", "description.length")
            .AddIf(
                request.DurationMinutes < SlotMinutes || request.DurationMinutes > MaxDurationMinutes || request.DurationMinutes % SlotMinutes != 0,
                "durationMinutes", "duration.invalid")
            .AddIf(request.Price < 0 || request.Price > MaxPrice, "price", "price.invalid")
            .AddIf(request.IsActive && locations.Count == 0, "locationIds", "locations.required");
    }
}
