using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace PrenotaConMe.Api.Hosting;

/// <summary>
/// In production the Angular build is published into wwwroot and served by the API itself
/// (single IIS site, same origin). In development wwwroot is empty and Angular runs on ng serve.
/// </summary>
internal static class SpaHostingExtensions
{
    // index.html and the Angular service worker files must always be revalidated,
    // otherwise clients keep running an old version of the PWA.
    private static readonly HashSet<string> NoCacheFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "index.html", "ngsw-worker.js", "ngsw.json", "manifest.webmanifest",
    };

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
                headers[HeaderNames.CacheControl] = NoCacheFiles.Contains(ctx.File.Name)
                    ? "no-cache"
                    // Angular emits content-hashed file names, safe to cache long-term.
                    : "public, max-age=31536000, immutable";
            },
        });
    }

    /// <summary>Client-side routes (/admin, /b/{slug}, ...) fall back to index.html; /api and /health never do.</summary>
    public static IEndpointRouteBuilder MapSpaFallback(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapFallbackToFile("{*path:regex(^(?!api/|api$|health).*$)}", "index.html");
        return endpoints;
    }
}
