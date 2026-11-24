using System.Text.RegularExpressions;

namespace PrenotaConMe.Modules.Tenancy.Domain;

/// <summary>Validation rules for tenant data. Error codes are stable keys the frontend translates.</summary>
public static partial class TenantRules
{
    public const int NameMaxLength = 100;
    public const int SlugMinLength = 3;
    public const int SlugMaxLength = 50;

    public static readonly IReadOnlySet<string> Languages = new HashSet<string>(StringComparer.Ordinal) { "it", "es", "en" };

    // Slugs that would collide with app routes or look official.
    private static readonly HashSet<string> ReservedSlugs = new(StringComparer.Ordinal)
    {
        "admin", "api", "app", "b", "health", "login", "logout", "register", "registrati",
        "www", "static", "assets", "i18n", "help", "support", "prenotaconme",
    };

    /// <summary>Lowercases and trims; does not otherwise alter the value (the user sees what is stored).</summary>
    public static string NormalizeSlug(string? slug) => (slug ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Returns an error code, or null when <paramref name="normalizedSlug"/> is valid.</summary>
    public static string? ValidateSlug(string normalizedSlug)
    {
        if (normalizedSlug.Length < SlugMinLength || normalizedSlug.Length > SlugMaxLength)
        {
            return "slug.length";
        }

        if (!SlugPattern().IsMatch(normalizedSlug))
        {
            return "slug.format";
        }

        return ReservedSlugs.Contains(normalizedSlug) ? "slug.reserved" : null;
    }

    /// <summary>Only IANA ids (e.g. "Europe/Rome"), valid on both Linux and Windows.</summary>
    public static bool IsValidTimeZone(string? timeZone) =>
        !string.IsNullOrWhiteSpace(timeZone)
        && TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZone, out _)
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _);

    public static bool IsValidLanguage(string? language) => language is not null && Languages.Contains(language);

    // Lowercase letters/digits separated by single hyphens; no leading/trailing hyphen.
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
