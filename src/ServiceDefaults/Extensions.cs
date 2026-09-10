using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;
public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(o => { o.IncludeFormattedMessage = true; o.IncludeScopes = true; });
        builder.Services.AddOpenTelemetry()
            .WithMetrics(m => m.AddMeter("CleanArchitecture.Application", "CleanArchitecture.Integrations")
                .AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation())
            .WithTracing(t => t.AddSource(builder.Environment.ApplicationName, "CleanArchitecture.Integrations")
                .AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());
        // Resilience is installed only by Infrastructure for explicitly classified operations.
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health").AllowAnonymous().WithMetadata(
            new Microsoft.AspNetCore.Http.EndpointSummaryAttribute("Check source readiness"),
            new Microsoft.AspNetCore.Http.EndpointDescriptionAttribute("Checks required customer-source availability and optional billing readiness. Does not return source credentials or exception details."));
        app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") }).AllowAnonymous().WithMetadata(
            new Microsoft.AspNetCore.Http.EndpointSummaryAttribute("Check application liveness"),
            new Microsoft.AspNetCore.Http.EndpointDescriptionAttribute("Reports whether the application is running, independently of external source availability."));
        return app;
    }
}
