using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PrenotaConMe.Shared.Hosting;

namespace PrenotaConMe.Api.Hosting;

/// <summary>F5-05: limits per user/IP. Section <c>RateLimiting</c>.</summary>
public sealed class RateLimitingOptions
{
    /// <summary>Requests per minute to /api for one signed-in user, or one IP address when anonymous.</summary>
    public int PerMinute { get; set; } = 300;

    /// <summary>Requests per hour to endpoints marked <see cref="RateLimits.Sensitive"/> (send e-mails, call other services).</summary>
    public int SensitivePerHour { get; set; } = 30;
}

/// <summary>F5-05: browser security headers. Section <c>Security</c>.</summary>
public sealed class SecurityOptions
{
    /// <summary>
    /// False sends the Content-Security-Policy as Report-Only: the browser console lists what it would block,
    /// nothing breaks. Turn it on after checking sign-in (Google, Microsoft), push and the calendar in staging.
    /// </summary>
    public bool EnforceCsp { get; set; }

    /// <summary>Firebase auth domain (e.g. "project.firebaseapp.com"), allowed as frame for the sign-in helper.</summary>
    public string? FirebaseAuthDomain { get; set; }
}

internal static partial class SecurityExtensions
{
    public static IServiceCollection AddSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitingOptions>(configuration.GetSection("RateLimiting"));
        services.Configure<SecurityOptions>(configuration.GetSection("Security"));

        // rtres.net hosts other sites: no includeSubDomains, no preload.
        services.AddHsts(o => o.MaxAge = TimeSpan.FromDays(180));

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RateLimiting");
                LogRejected(logger, context.HttpContext.Request.Path);
                // Coded title: the frontend translates it (errors.request.tooMany).
                await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "request.tooMany")
                    .ExecuteAsync(context.HttpContext);
            };

            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                if (!http.Request.Path.StartsWithSegments("/api") || IsWebhook(http.Request.Path))
                {
                    return RateLimitPartition.GetNoLimiter("unlimited");
                }

                var limits = http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(PartitionKey(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.PerMinute,
                    Window = TimeSpan.FromMinutes(1),
                });
            });

            o.AddPolicy(RateLimits.Sensitive, http =>
            {
                var limits = http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter("sensitive:" + PartitionKey(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.SensitivePerHour,
                    Window = TimeSpan.FromHours(1),
                });
            });
        });

        return services;
    }

    /// <summary>Headers on every response; place first so error pages and static files get them too.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<SecurityOptions>>().Value;
        var csp = ContentSecurityPolicy(options.FirebaseAuthDomain);
        var cspHeader = options.EnforceCsp ? "Content-Security-Policy" : "Content-Security-Policy-Report-Only";

        return app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                // Not "same-origin": the Google/Microsoft sign-in popups must be able to report back.
                headers["Cross-Origin-Opener-Policy"] = "same-origin-allow-popups";

                // Only the SPA's HTML: the Hangfire dashboard and Scalar bring their own inline scripts.
                var path = context.Request.Path;
                if (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true
                    && !path.StartsWithSegments("/hangfire") && !path.StartsWithSegments("/scalar"))
                {
                    headers[cspHeader] = csp;
                }

                return Task.CompletedTask;
            });
            return next(context);
        });
    }

    /// <summary>
    /// What the Angular app needs: Firebase Auth (Google APIs, the auth domain's iframe), Google Fonts, inline styles
    /// (Angular Material, FullCalendar) and the critical-CSS loader (<c>onload="this.media='all'"</c>, allowed by hash).
    /// </summary>
    internal static string ContentSecurityPolicy(string? firebaseAuthDomain)
    {
        var authFrame = string.IsNullOrWhiteSpace(firebaseAuthDomain) ? "" : $" https://{firebaseAuthDomain}";
        return string.Join("; ",
            "default-src 'self'",
            "base-uri 'self'",
            "object-src 'none'",
            "frame-ancestors 'none'",
            "form-action 'self'",
            "script-src 'self' 'unsafe-hashes' 'sha256-MhtPZXr7+LpJUY5qtMutB+qWfQtMaPccfe7QXtCcEYc=' https://apis.google.com https://www.gstatic.com",
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
            "font-src 'self' https://fonts.gstatic.com",
            "img-src 'self' data: blob: https://*.googleusercontent.com",
            "connect-src 'self' https://*.googleapis.com https://apis.google.com",
            $"frame-src https://accounts.google.com{authFrame}",
            "worker-src 'self'",
            "manifest-src 'self'");
    }

    /// <summary>Channels that call us in bursts and authenticate with their own secret (Telegram, later WhatsApp).</summary>
    private static bool IsWebhook(PathString path) => path.Value?.Contains("/webhook", StringComparison.OrdinalIgnoreCase) == true;

    private static string PartitionKey(HttpContext http) =>
        http.User.FindFirstValue("sub") is { Length: > 0 } uid
            ? "user:" + uid
            : "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limit reached on {Path}")]
    private static partial void LogRejected(ILogger logger, PathString path);
}
