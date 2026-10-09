using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MailMerge.Api.Host;

/// <summary>
/// OpenTelemetry logs, metrics and traces, exported over OTLP when an endpoint is configured (the
/// <c>OTLP_Endpoint</c> connection string, or the standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> variable).
/// </summary>
internal static class ObservabilityExtensions
{
    public static void AddObservability(this WebApplicationBuilder builder)
    {
        var serviceVersion = typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString() ?? "unknown";

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(AppTelemetry.SourceName, serviceVersion: serviceVersion,
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes([new("deployment.environment", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing => tracing
                .AddSource(AppTelemetry.SourceName)
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(metrics => metrics
                .AddMeter(AppTelemetry.SourceName)
                .AddNpgsqlInstrumentation()
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        var endpoint = builder.Configuration.GetConnectionString("OTLP_Endpoint");
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            otel.UseOtlpExporter(OtlpExportProtocol.Grpc, new Uri(endpoint));
        }
        else if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }
    }
}
