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

/// <summary>
/// Endpoint metadata: the caller may send the bearer token (<c>access_token</c>) and the tenant slug (<c>tenant</c>)
/// in the query string, because browsers cannot set headers on WebSocket and server-sent-events connections (SignalR).
/// </summary>
public sealed class CredentialsInQueryMetadata
{
    public static readonly CredentialsInQueryMetadata Instance = new();

    private CredentialsInQueryMetadata()
    {
    }
}

public static class TenantEndpointExtensions
{
    /// <summary>Query parameter with the tenant slug on endpoints marked with <see cref="AcceptCredentialsInQuery"/>.</summary>
    public const string TenantQuery = "tenant";

    /// <summary>Query parameter with the bearer token on endpoints marked with <see cref="AcceptCredentialsInQuery"/>.</summary>
    public const string AccessTokenQuery = "access_token";

    /// <summary>Lets the endpoint read the token and tenant from the query string when the headers are missing.</summary>
    public static TBuilder AcceptCredentialsInQuery<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(CredentialsInQueryMetadata.Instance);

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
