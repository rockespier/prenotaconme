using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using PrenotaConMe.Modules.Tenancy;

namespace PrenotaConMe.IntegrationTests;

/// <summary>F5-05: security headers and rate limits.</summary>
public sealed class SecurityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Responses_carry_the_security_headers()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/api/info", UriKind.Relative));

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("same-origin-allow-popups", response.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        // JSON responses get no CSP: only the SPA's HTML does.
        Assert.False(response.Headers.Contains("Content-Security-Policy-Report-Only"));
    }

    [Fact]
    public async Task Too_many_requests_get_429_with_retry_after()
    {
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:PerMinute", "3"));
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await client.GetAsync(new Uri("/api/info", UriKind.Relative))).StatusCode);
        }

        var last = await client.GetAsync(new Uri("/api/info", UriKind.Relative));
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.True(last.Headers.RetryAfter is not null);
        Assert.Contains("request.tooMany", await last.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/health/live", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Sensitive_endpoints_have_a_lower_hourly_limit()
    {
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:SensitivePerHour", "1"));
        var client = TestAuthHandlerClient(limited, "spammer");

        var first = await client.PostAsJsonAsync("/api/tenancy/tenants", new RegisterTenantRequest("A", $"a-{Guid.NewGuid():N}"[..20], "Europe/Rome", "it"));
        var second = await client.PostAsJsonAsync("/api/tenancy/tenants", new RegisterTenantRequest("B", $"b-{Guid.NewGuid():N}"[..20], "Europe/Rome", "it"));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    private static HttpClient TestAuthHandlerClient(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app, string uid)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.Header, $"{uid}|{uid}@test.local|{uid}");
        return client;
    }
}
