using Microsoft.AspNetCore.Builder;

namespace PrenotaConMe.Shared.Tenancy;

/// <summary>Endpoint metadata: the endpoint only works with a resolved tenant.</summary>
public sealed class RequiresTenantMetadata
{
    public static readonly RequiresTenantMetadata Instance = new();

    private RequiresTenantMetadata()
    {
    }
}

public static class TenantEndpointExtensions
{
    /// <summary>Header the client sends with the tenant (business) slug.</summary>
    public const string TenantHeader = "X-Tenant";

    /// <summary>
    /// Marks the endpoint as tenant-scoped. The tenant resolution middleware answers 400 when the
    /// <see cref="TenantHeader"/> header is missing (before authorization runs).
    /// </summary>
    public static TBuilder RequireTenant<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(RequiresTenantMetadata.Instance);
}
