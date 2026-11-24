using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace PrenotaConMe.Api.Hosting;

/// <summary>
/// In production the Angular build is published into wwwroot and served by the API itself
/// (single IIS site, same origin). In development wwwroot is empty and Angular runs on ng serve.
/// </summary>
internal static partial class SpaHostingExtensions
{
    /// <summary>
    /// Files whose name carries a content hash (main-ABC123XY.js, chunk-*.js, styles-*.css, hashed media):
    /// their content never changes, so they can be cached for a year. Everything else (index.html,
    /// i18n/*.json, ngsw files, manifest, icons) must be revalidated, or clients keep stale copies.
    /// </summary>
    internal static bool IsContentHashed(string fileName) => HashedFileName().IsMatch(fileName);

    [System.Text.RegularExpressions.GeneratedRegex(@"-[A-Z0-9]{8}\.(js|css|woff2?|ttf|eot|svg|png|jpe?g|gif|webp|avif|ico)$")]
    private static partial System.Text.RegularExpressions.Regex HashedFileName();

    public static IApplicationBuilder UseSpaStaticFiles(this IApplicationBuilder app)
    {
        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".webmanifest"] = "application/manifest+json";

        return app.UseDefaultFiles().UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = contentTypes,
            OnPrepareResponse = ctx =>
            {
                var headers = ctx.Context.Response.Headers;
                headers[HeaderNames.CacheControl] = IsContentHashed(ctx.File.Name)
                    ? "public, max-age=31536000, immutable"
                    : "no-cache";
            },
        });
    }

    /// <summary>
    /// Client-side routes (/admin, /{business-slug}, ...) fall back to index.html; /api, /health and file paths never do.
    /// "nonfile" is essential: the static file middleware skips requests that already matched an endpoint,
    /// so without it main-*.js / manifest.webmanifest would be answered with index.html.
    /// </summary>
    public static IEndpointRouteBuilder MapSpaFallback(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapFallbackToFile("{*path:nonfile:regex(^(?!api/|api$|health).*$)}", "index.html");
        return endpoints;
    }
}
