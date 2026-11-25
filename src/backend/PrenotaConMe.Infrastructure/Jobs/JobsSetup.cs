using Hangfire;
using Hangfire.Common;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PrenotaConMe.Infrastructure.Jobs;

/// <summary>"Jobs" configuration section.</summary>
public sealed class JobsOptions
{
    /// <summary>False keeps enqueueing working but runs no jobs in this process.</summary>
    public bool Server { get; set; } = true;

    public int WorkerCount { get; set; } = 5;

    /// <summary>Fallback poll for new jobs; LISTEN/NOTIFY usually wakes the workers sooner.</summary>
    public TimeSpan QueuePollInterval { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>F2-01: Hangfire with its tables in the "hangfire" schema of the app database.</summary>
public static class JobsSetup
{
    public const string Schema = "hangfire";

    public static IServiceCollection AddJobs(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        var options = configuration.GetSection("Jobs").Get<JobsOptions>() ?? new JobsOptions();

        services.AddSingleton<JobActivator, TenantJobActivator>();

        // The storage is only built when first resolved (after Build), so generating the OpenAPI document at
        // build time does not need a database.
        services.AddHangfire((sp, config) => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseActivator(sp.GetRequiredService<JobActivator>())
            .UsePostgreSqlStorage(
                o => o.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions
                {
                    SchemaName = Schema,
                    PrepareSchemaIfNecessary = true,
                    QueuePollInterval = options.QueuePollInterval,
                    EnableLongPolling = true,
                }));

        // Jobs enqueued from a request run in the tenant of that request.
        services.AddSingleton(sp => new BackgroundJobClient(
            sp.GetRequiredService<JobStorage>(),
            sp.GetRequiredService<IJobFilterProvider>()));
        services.AddScoped<IBackgroundJobClient, TenantBackgroundJobClient>();

        // No processing without a database (OpenAPI document generation at build time starts hosted services).
        if (options.Server && !string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddHangfireServer(o => o.WorkerCount = options.WorkerCount);
        }

        return services;
    }
}
