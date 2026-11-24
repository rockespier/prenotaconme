using System.Security.Claims;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using PrenotaConMe.Shared.Auth;

namespace PrenotaConMe.Api.Hosting;

/// <summary>
/// F2-01: Hangfire dashboard at /hangfire, for platform super-admins only (it shows the jobs of every business).
/// The browser cannot send the Firebase bearer token on a page navigation, so the SPA first calls
/// POST /api/jobs/dashboard-session with the token, which sets a short-lived cookie scoped to /hangfire.
/// </summary>
internal static class JobsDashboardExtensions
{
    public const string Path = "/hangfire";
    private const string Scheme = "JobsDashboard";

    public static IServiceCollection AddJobsDashboard(this IServiceCollection services)
    {
        services.AddAuthentication().AddCookie(Scheme, o =>
        {
            o.Cookie.Name = "prenota_jobs";
            o.Cookie.Path = Path;
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromHours(1);
            o.SlidingExpiration = false;
        });
        return services;
    }

    public static void MapJobsDashboard(this WebApplication app, IEndpointRouteBuilder api)
    {
        api.MapPost("/jobs/dashboard-session", OpenSessionAsync)
            .RequireAuthorization()
            .WithName("OpenJobsDashboard")
            .WithTags("System");

        app.MapHangfireDashboard(Path, new DashboardOptions
        {
            DashboardTitle = "PrenotaConMe · Jobs",
            AppPath = "/admin",
            Authorization = [],
            AsyncAuthorization = [new CookieAuthorizationFilter()],
        });
    }

    private static async Task<Results<NoContent, ForbidHttpResult>> OpenSessionAsync(
        HttpContext http, ICurrentUser user, PlatformAdmins admins)
    {
        if (!admins.IsAdmin(user))
        {
            return TypedResults.Forbid();
        }

        var identity = new ClaimsIdentity([new Claim("sub", user.ExternalId!), new Claim("email", user.Email!)], Scheme);
        await http.SignInAsync(Scheme, new ClaimsPrincipal(identity));
        return TypedResults.NoContent();
    }

    private sealed class CookieAuthorizationFilter : IDashboardAsyncAuthorizationFilter
    {
        public async Task<bool> AuthorizeAsync(DashboardContext context) =>
            (await context.GetHttpContext().AuthenticateAsync(Scheme)).Succeeded;
    }
}
