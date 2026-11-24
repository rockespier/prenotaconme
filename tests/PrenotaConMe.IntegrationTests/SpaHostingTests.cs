using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace PrenotaConMe.IntegrationTests;

/// <summary>The API serves the Angular build from wwwroot (production on IIS).</summary>
public sealed class SpaHostingTests(ApiFactory factory) : IClassFixture<ApiFactory>, IDisposable
{
    private readonly string _webRoot = CreateWebRoot();

    private HttpClient CreateClient() =>
        factory.WithWebHostBuilder(b => b.UseSetting(WebHostDefaults.WebRootKey, _webRoot)).CreateClient();

    [Theory]
    [InlineData("/main-ABCD1234.js", "text/javascript")]
    [InlineData("/manifest.webmanifest", "application/manifest+json")]
    public async Task Static_files_are_served_with_their_content_type(string path, string contentType)
    {
        var response = await CreateClient().GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/main-ABCD1234.js", "public, max-age=31536000, immutable")]
    [InlineData("/i18n/it.json", "no-cache")] // not content-hashed: a stale copy would hide new translations
    [InlineData("/manifest.webmanifest", "no-cache")]
    [InlineData("/", "no-cache")]
    public async Task Only_content_hashed_files_are_cached_long_term(string path, string cacheControl)
    {
        var response = await CreateClient().GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(cacheControl, response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Missing_file_is_404_not_index_html()
    {
        var response = await CreateClient().GetAsync(new Uri("/chunk-MISSING.js", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/admin")]
    [InlineData("/barberia-mario")]
    public async Task Client_routes_fall_back_to_index_html(string path)
    {
        var body = await CreateClient().GetStringAsync(new Uri(path, UriKind.Relative));

        Assert.Contains("<app-root>", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_api_route_is_not_index_html()
    {
        var response = await CreateClient().GetAsync(new Uri("/api/nope", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);

    private static string CreateWebRoot()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"prenota-wwwroot-{Guid.NewGuid():N}")).FullName;
        File.WriteAllText(Path.Combine(dir, "index.html"), "<!doctype html><html><body><app-root></app-root></body></html>");
        File.WriteAllText(Path.Combine(dir, "main-ABCD1234.js"), "console.log('ok');");
        Directory.CreateDirectory(Path.Combine(dir, "i18n"));
        File.WriteAllText(Path.Combine(dir, "i18n", "it.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "manifest.webmanifest"), "{}");
        return dir;
    }
}
