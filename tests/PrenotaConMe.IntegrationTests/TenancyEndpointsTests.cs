using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrenotaConMe.Modules.Tenancy;
using PrenotaConMe.Modules.Users;

namespace PrenotaConMe.IntegrationTests;

public sealed class TenancyEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static string NewSlug() => $"t-{Guid.NewGuid():N}"[..20];

    private static RegisterTenantRequest Request(string slug) => new("Barberia Test", slug, "Europe/Rome", "it");

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, string slug) =>
        await client.PostAsJsonAsync("/api/tenancy/tenants", Request(slug));

    [Fact]
    public async Task Register_requires_authentication()
    {
        var response = await RegisterAsync(factory.CreateClient(), NewSlug());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_creates_tenant_and_makes_caller_admin()
    {
        var slug = NewSlug();
        var owner = factory.CreateUserClient("owner-" + slug);

        var response = await RegisterAsync(owner, slug);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var tenant = await response.Content.ReadFromJsonAsync<TenantDto>(Json);
        Assert.Equal(slug, tenant!.Slug);

        var me = await owner.GetFromJsonAsync<MeDto>("/api/users/me", Json);
        var membership = Assert.Single(me!.Memberships);
        Assert.Equal(slug, membership.TenantSlug);
        Assert.Equal(PrenotaConMe.Shared.Auth.TenantRole.Admin, membership.Role);

        var publicInfo = await factory.CreateClient().GetFromJsonAsync<PublicTenantDto>($"/api/tenancy/tenants/{slug}", Json);
        Assert.Equal("Barberia Test", publicInfo!.Name);
    }

    [Fact]
    public async Task Register_duplicate_slug_returns_conflict()
    {
        var slug = NewSlug();
        await RegisterAsync(factory.CreateUserClient("a-" + slug), slug);

        var response = await RegisterAsync(factory.CreateUserClient("b-" + slug), slug);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_invalid_data_returns_validation_problem()
    {
        var client = factory.CreateUserClient("invalid-user");

        var response = await client.PostAsJsonAsync("/api/tenancy/tenants", new RegisterTenantRequest("", "Admin", "Mars/Base", "fr"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("slug.reserved", body, StringComparison.Ordinal);
        Assert.Contains("timeZone.invalid", body, StringComparison.Ordinal);
        Assert.Contains("language.invalid", body, StringComparison.Ordinal);
        Assert.Contains("name.required", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Slug_availability_reflects_registrations()
    {
        var slug = NewSlug();
        var anonymous = factory.CreateClient();

        var before = await anonymous.GetFromJsonAsync<SlugAvailabilityDto>($"/api/tenancy/slugs/{slug}/availability", Json);
        await RegisterAsync(factory.CreateUserClient("avail-" + slug), slug);
        var after = await anonymous.GetFromJsonAsync<SlugAvailabilityDto>($"/api/tenancy/slugs/{slug}/availability", Json);

        Assert.True(before!.Available);
        Assert.False(after!.Available);
        Assert.Equal("slug.taken", after.Reason);
    }

    [Fact]
    public async Task Unknown_tenant_header_returns_not_found()
    {
        var client = factory.CreateUserClient("someone", tenantSlug: "does-not-exist");

        var response = await client.GetAsync(new Uri("/api/tenancy/tenants/current", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Current_tenant_requires_tenant_header()
    {
        var response = await factory.CreateUserClient("no-header")
            .GetAsync(new Uri("/api/tenancy/tenants/current", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Current_tenant_is_only_visible_to_its_members()
    {
        var slug = NewSlug();
        var ownerUid = "owner2-" + slug;
        await RegisterAsync(factory.CreateUserClient(ownerUid), slug);

        var asOwner = await factory.CreateUserClient(ownerUid, slug)
            .GetAsync(new Uri("/api/tenancy/tenants/current", UriKind.Relative));
        var asStranger = await factory.CreateUserClient("stranger-" + slug, slug)
            .GetAsync(new Uri("/api/tenancy/tenants/current", UriKind.Relative));
        var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Tenant", slug);
        var asAnonymous = await anonymous.GetAsync(new Uri("/api/tenancy/tenants/current", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, asOwner.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asStranger.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asAnonymous.StatusCode);
    }
}
