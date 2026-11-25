using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace PrenotaConMe.Api.Hosting;

internal static class ObservabilityExtensions
{
    /// <summary>
    /// OpenTelemetry traces + metrics. Exported via OTLP only when OpenTelemetry:OtlpEndpoint is set
    /// (Seq: http://localhost:5341/ingest/otlp).
    /// </summary>
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("prenotaconme-api"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                    // The Telegram Bot API carries the bot token in the URL path: never trace those requests.
                    .AddHttpClientInstrumentation(o => o.FilterHttpRequestMessage = r => r.RequestUri?.Host != "api.telegram.org")
                    .AddNpgsql();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri($"{otlpEndpoint.TrimEnd('/')}/v1/traces");
                        o.Protocol = OtlpExportProtocol.HttpProtobuf;
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri($"{otlpEndpoint.TrimEnd('/')}/v1/metrics");
                        o.Protocol = OtlpExportProtocol.HttpProtobuf;
                    });
                }
            });

        return services;
    }
}
