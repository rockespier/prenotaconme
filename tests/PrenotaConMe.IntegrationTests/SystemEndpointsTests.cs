using System.Net;

namespace PrenotaConMe.IntegrationTests;

public sealed class SystemEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/api/info")]
    [InlineData("/openapi/v1.json")]
    public async Task Endpoint_returns_ok(string path)
    {
        var response = await _client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_reports_database_healthy_after_migrations()
    {
        var body = await _client.GetStringAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal("Healthy", body);
    }
}
