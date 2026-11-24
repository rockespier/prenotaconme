using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace PrenotaConMe.Api.Hosting;

internal static class AuthExtensions
{
    /// <summary>
    /// Validates Firebase Authentication ID tokens (Google sign-in). Signing keys are fetched from
    /// the OpenID metadata of https://securetoken.google.com/{projectId}. Firebase:ProjectId is not a secret.
    /// </summary>
    public static IServiceCollection AddFirebaseAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var projectId = configuration["Firebase:ProjectId"];
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new InvalidOperationException("Configuration value 'Firebase:ProjectId' is missing.");
        }

        var issuer = $"https://securetoken.google.com/{projectId}";

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = issuer;
                // Keep raw JWT claim names ("sub", "email", "name", "picture").
                options.MapInboundClaims = false;
                options.TokenValidationParameters.ValidIssuer = issuer;
                options.TokenValidationParameters.ValidAudience = projectId;
                options.TokenValidationParameters.NameClaimType = "name";
            });

        services.AddAuthorization();
        return services;
    }
}
