namespace Microsoft.Extensions.Hosting;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

/// <summary>
/// Defaults shared by every Flowgent service: health checks, OpenTelemetry (logs, traces, metrics),
/// service discovery and resilience for outgoing HTTP calls.
/// </summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>Health check route: all checks must pass.</summary>
    public const string HealthEndpointPath = "/health";

    /// <summary>Liveness route: only checks tagged "live".</summary>
    public const string AlivenessEndpointPath = "/alive";

    /// <summary>Activity sources and meters emitted by Flowgent libraries.</summary>
    public const string FlowgentTelemetryPrefix = "Flowgent.*";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(FlowgentTelemetryPrefix, "Microsoft.Agents.AI*", "Experimental.Microsoft.Extensions.AI*"))
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddSource(FlowgentTelemetryPrefix, "Microsoft.Agents.AI*", "Experimental.Microsoft.Extensions.AI*")
                .AddAspNetCoreInstrumentation(options =>
                    // Health probes would flood the trace view.
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                        && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                .AddHttpClientInstrumentation());

        // The OTLP exporter is enabled only when an endpoint is configured (Aspire sets it).
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>Maps <c>/health</c> (readiness) and <c>/alive</c> (liveness) in every environment.</summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthEndpointPath);
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live"),
        });

        return app;
    }
}
