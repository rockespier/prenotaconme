using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PrenotaConMe.IntegrationTests;

/// <summary>
/// Replaces Firebase JWT validation in tests. Header "X-Test-User: uid[|email[|name]]" authenticates
/// with the same raw claim names Firebase tokens carry ("sub", "email", "name").
/// </summary>
internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string Header = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var value = Request.Headers[Header].ToString();
        if (string.IsNullOrEmpty(value))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var parts = value.Split('|');
        var claims = new List<Claim> { new("sub", parts[0]) };
        if (parts.Length > 1)
        {
            claims.Add(new Claim("email", parts[1]));
            claims.Add(new Claim("email_verified", "true"));
        }
        if (parts.Length > 2) claims.Add(new Claim("name", parts[2]));

        var identity = new ClaimsIdentity(claims, SchemeName, "name", "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
