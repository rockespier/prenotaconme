using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Api.Hosting;
using PrenotaConMe.Infrastructure;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Tenancy;
using Scalar.AspNetCore;
using Serilog;

// Startup/fatal logger only. Not a bootstrap (reloadable) logger: those can be frozen once per process,
// which breaks multiple WebApplicationFactory hosts in the integration tests.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddObservability(builder.Configuration);
    builder.Services.AddProblemDetails();
    builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddFirebaseAuthentication(builder.Configuration);
    builder.Services.AddOpenApi();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddModules(builder.Configuration, ModuleCatalog.All);

    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

    var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

    var app = builder.Build();

    if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSerilogRequestLogging();
    app.UseSpaStaticFiles();
    app.UseCors();
    app.UseAuthentication();
    app.UseTenantResolution();
    app.UseAuthorization();

    app.MapOpenApi();
    if (app.Environment.IsDevelopment())
    {
        app.MapScalarApiReference();
    }

    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

    var api = app.MapGroup("/api");
    api.MapGet("/info", (IHostEnvironment env) => new AppInfo(
            "PrenotaConMe",
            typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            env.EnvironmentName))
        .WithName("GetAppInfo")
        .WithTags("System");

    api.MapModules(ModuleCatalog.All);

    app.MapSpaFallback();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

internal sealed record AppInfo(string Name, string Version, string Environment);
