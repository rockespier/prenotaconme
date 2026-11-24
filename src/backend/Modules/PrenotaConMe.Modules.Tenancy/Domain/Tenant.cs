using PrenotaConMe.Shared.Domain;

namespace PrenotaConMe.Modules.Tenancy.Domain;

/// <summary>A business using the platform. Not tenant-owned itself: it *is* the tenant.</summary>
public sealed class Tenant : Entity
{
    private Tenant()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Unique, URL-safe identifier used in the public booking page (/b/{slug}) and the X-Tenant header.</summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>IANA time zone (e.g. "Europe/Rome"); default for locations of this business.</summary>
    public string TimeZone { get; private set; } = string.Empty;

    public string DefaultLanguage { get; private set; } = "it";

    public bool IsActive { get; private set; } = true;

    /// <summary>Inputs must already be validated with <see cref="TenantRules"/>.</summary>
    public static Tenant Create(string name, string normalizedSlug, string timeZone, string language) => new()
    {
        Name = name.Trim(),
        Slug = normalizedSlug,
        TimeZone = timeZone,
        DefaultLanguage = language,
    };
}
