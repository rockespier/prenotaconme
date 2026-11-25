using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Infrastructure.Jobs;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Database";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing.");

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<AuditingInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            ConfigureNpgsql(options, connectionString);
            options.AddInterceptors(sp.GetRequiredService<AuditingInterceptor>());
        });

        // Keys in the database: encrypted values stay readable after restarts and redeploys. Skipped without a
        // connection string (OpenAPI document generation at build time).
        var dataProtection = services.AddDataProtection().SetApplicationName("PrenotaConMe");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            dataProtection.PersistKeysToDbContext<AppDbContext>();
        }

        services.AddJobs(configuration, connectionString);
        return services;
    }

    public static void ConfigureNpgsql(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
