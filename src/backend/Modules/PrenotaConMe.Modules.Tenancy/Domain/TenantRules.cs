using System.Text.RegularExpressions;
using PrenotaConMe.Shared.Scheduling;

namespace PrenotaConMe.Modules.Tenancy.Domain;

/// <summary>Validation rules for tenant data. Error codes are stable keys the frontend translates.</summary>
public static partial class TenantRules
{
    public const int NameMaxLength = 100;
    public const int SlugMinLength = 3;
    public const int SlugMaxLength = 50;

    public const int DefaultCancellationNoticeHours = 2;

    /// <summary>Upper bound of the cancellation notice an Admin can set (30 days).</summary>
    public const int MaxCancellationNoticeHours = 720;

    public static readonly IReadOnlySet<string> Languages = new HashSet<string>(StringComparer.Ordinal) { "it", "es", "en" };

    /// <summary>
    /// Business pages live at the site root (/{slug}), so slugs must never collide with app routes.
    /// Keep in sync with the top-level routes in src/frontend/prenotaconme-web/src/app/app.routes.ts
    /// and the server paths (/api, /health, /openapi, /scalar). Also blocks names that look official.
    /// </summary>
    public static readonly IReadOnlySet<string> ReservedSlugs = new HashSet<string>(StringComparer.Ordinal)
    {
        // Current app and server routes
        "admin", "login", "registra-attivita", "b", "api", "health", "openapi", "scalar",
        // Likely future routes
        "app", "account", "auth", "logout", "signin", "signup", "register", "registrati", "registrazione",
        "dashboard", "settings", "impostazioni", "profilo", "profile", "prenotazioni", "bookings",
        "privacy", "terms", "termini", "cookie", "cookies", "contatti", "contact", "about", "chi-siamo",
        "pricing", "prezzi", "blog", "help", "aiuto", "support", "supporto", "faq", "status",
        // Static assets and infrastructure
        "assets", "static", "media", "i18n", "icons", "favicon", "manifest", "www", "mail", "ftp",
        // Brand
        "prenotaconme", "rtres",
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
    public static bool IsValidTimeZone(string? timeZone) => TimeZones.IsValidIana(timeZone);

    public static bool IsValidLanguage(string? language) => language is not null && Languages.Contains(language);

    // Lowercase letters/digits separated by single hyphens; no leading/trailing hyphen.
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
