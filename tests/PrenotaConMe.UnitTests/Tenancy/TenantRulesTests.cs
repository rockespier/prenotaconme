using PrenotaConMe.Modules.Tenancy.Domain;

namespace PrenotaConMe.UnitTests.Tenancy;

public sealed class TenantRulesTests
{
    [Theory]
    [InlineData("barberia-mario")]
    [InlineData("abc")]
    [InlineData("salon-2-roma")]
    public void ValidateSlug_accepts_valid_slugs(string slug) => Assert.Null(TenantRules.ValidateSlug(slug));

    [Theory]
    [InlineData("ab", "slug.length")]
    [InlineData("-barberia", "slug.format")]
    [InlineData("barberia-", "slug.format")]
    [InlineData("barber--ia", "slug.format")]
    [InlineData("barbería", "slug.format")]
    [InlineData("my_salon", "slug.format")]
    [InlineData("admin", "slug.reserved")]
    [InlineData("api", "slug.reserved")]
    public void ValidateSlug_rejects_invalid_slugs(string slug, string expected) =>
        Assert.Equal(expected, TenantRules.ValidateSlug(slug));

    [Fact]
    public void ValidateSlug_rejects_too_long() =>
        Assert.Equal("slug.length", TenantRules.ValidateSlug(new string('a', TenantRules.SlugMaxLength + 1)));

    [Fact]
    public void NormalizeSlug_trims_and_lowercases() =>
        Assert.Equal("barberia-mario", TenantRules.NormalizeSlug("  Barberia-Mario "));

    [Theory]
    [InlineData("Europe/Rome", true)]
    [InlineData("America/Lima", true)]
    [InlineData("W. Europe Standard Time", false)]
    [InlineData("Mars/Olympus", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidTimeZone_only_accepts_iana_ids(string? tz, bool expected) =>
        Assert.Equal(expected, TenantRules.IsValidTimeZone(tz));

    [Theory]
    [InlineData("it", true)]
    [InlineData("es", true)]
    [InlineData("en", true)]
    [InlineData("fr", false)]
    [InlineData(null, false)]
    public void IsValidLanguage(string? lang, bool expected) => Assert.Equal(expected, TenantRules.IsValidLanguage(lang));
}
