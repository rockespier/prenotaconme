using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PrenotaConMe.Modules.Tenancy;

namespace PrenotaConMe.IntegrationTests;

/// <summary>F5-08: logo and cover of the business on its booking page.</summary>
public sealed class TenantImageTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, string kind, byte[] data, string fileName = "image.png")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", fileName);
        return client.PutAsync(new Uri($"/api/tenancy/tenants/current/images/{kind}", UriKind.Relative), content);
    }

    [Fact]
    public async Task Admin_uploads_a_logo_that_the_booking_page_shows()
    {
        var business = await TestBusiness.CreateAsync(factory);

        var response = await UploadAsync(business.Owner, "logo", Png);

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var tenant = (await response.Content.ReadFromJsonAsync<TenantDto>(TestJson.Options))!;
        Assert.NotNull(tenant.LogoUrl);
        Assert.Null(tenant.CoverUrl);

        var anonymous = factory.CreateClient();
        var page = (await anonymous.GetFromJsonAsync<PublicTenantDto>($"/api/tenancy/tenants/{business.Slug}", TestJson.Options))!;
        Assert.Equal(tenant.LogoUrl, page.LogoUrl);

        var image = await anonymous.GetAsync(new Uri(page.LogoUrl!, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.True(image.Headers.CacheControl?.Public);
        Assert.Equal(Png, await image.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Only_png_jpeg_or_webp_up_to_one_megabyte_are_accepted()
    {
        var business = await TestBusiness.CreateAsync(factory);

        var svg = await UploadAsync(business.Owner, "cover", "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), "x.svg");
        var tooBig = await UploadAsync(business.Owner, "cover", [.. Png, .. new byte[1024 * 1024]]);

        Assert.Equal(HttpStatusCode.BadRequest, svg.StatusCode);
        Assert.Contains("image.type", await svg.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Contains("image.size", await tooBig.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Removing_the_cover_hides_it()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var uploaded = (await (await UploadAsync(business.Owner, "cover", Png)).Content.ReadFromJsonAsync<TenantDto>(TestJson.Options))!;

        var removed = await business.Owner.DeleteAsync(new Uri("/api/tenancy/tenants/current/images/cover", UriKind.Relative));

        Assert.Null((await removed.Content.ReadFromJsonAsync<TenantDto>(TestJson.Options))!.CoverUrl);
        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.CreateClient().GetAsync(new Uri(uploaded.CoverUrl!, UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Clients_cannot_change_the_images()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var client = factory.CreateUserClient("client-" + business.Slug, business.Slug);

        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(client, "logo", Png)).StatusCode);
    }
}
