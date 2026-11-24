using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Tenancy;

/// <summary>
/// Resolves the tenant from the <see cref="TenantEndpointExtensions.TenantHeader"/> header (business slug)
/// and stores it in the scoped <see cref="ITenantContext"/>. Unknown or inactive slugs end the request with 404.
/// Requests without the header continue with no tenant, except endpoints marked with RequireTenant() (400).
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantDirectory directory, ITenantContext tenantContext)
    {
        var slug = context.Request.Headers[TenantEndpointExtensions.TenantHeader].ToString();

        if (!string.IsNullOrWhiteSpace(slug))
        {
            var tenant = await directory.FindBySlugAsync(slug, context.RequestAborted);
            if (tenant is not { IsActive: true })
            {
                await Results.Problem(
                        statusCode: StatusCodes.Status404NotFound,
                        title: "Tenant not found",
                        detail: $"No active business with slug '{slug}'.")
                    .ExecuteAsync(context);
                return;
            }

            tenantContext.SetTenant(tenant.Id);
        }
        else if (context.GetEndpoint()?.Metadata.GetMetadata<RequiresTenantMetadata>() is not null)
        {
            await Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Tenant required",
                    detail: $"Send the business slug in the '{TenantEndpointExtensions.TenantHeader}' header.")
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}

public static class TenantResolutionExtensions
{
    /// <summary>Place after authentication and before authorization (tenant role checks need the tenant).</summary>
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolutionMiddleware>();
}
