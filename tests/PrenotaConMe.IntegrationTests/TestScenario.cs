using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrenotaConMe.Modules.Tenancy;

namespace PrenotaConMe.IntegrationTests;

internal static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>A freshly registered business with its owner (Admin) client.</summary>
internal sealed record TestBusiness(string Slug, string OwnerUid, HttpClient Owner)
{
    public static async Task<TestBusiness> CreateAsync(ApiFactory factory, string language = "it")
    {
        var slug = $"t-{Guid.NewGuid():N}"[..20];
        var ownerUid = "owner-" + slug;

        var registration = await factory.CreateUserClient(ownerUid)
            .PostAsJsonAsync("/api/tenancy/tenants", new RegisterTenantRequest("Test business", slug, "Europe/Rome", language));
        registration.EnsureSuccessStatusCode();

        return new TestBusiness(slug, ownerUid, factory.CreateUserClient(ownerUid, slug));
    }

    public async Task<T> GetAsync<T>(string url) =>
        (await Owner.GetFromJsonAsync<T>(url, TestJson.Options))!;
}
