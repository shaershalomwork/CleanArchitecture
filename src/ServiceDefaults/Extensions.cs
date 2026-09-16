using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using CleanArchitecture.ServiceDefaults.Logging;
using Microsoft.Extensions.Logging.Console;
using System.Reflection;

namespace Microsoft.Extensions.Hosting;
public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var identity = new ServiceLogIdentity(builder.Configuration["OTEL_SERVICE_NAME"] ?? "webapi",
            builder.Environment.EnvironmentName,
            builder.Configuration["Observability:ServiceVersion"] ?? Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
            Environment.MachineName);
        builder.Services.AddSingleton(identity);
        var resource = ResourceBuilder.CreateEmpty().AddService(identity.Name, serviceVersion: identity.Version, serviceInstanceId: identity.InstanceId)
            .AddAttributes([new("deployment.environment.name", identity.Environment)]);
        var resourceAttributes = resource.Build().Attributes.ToArray();
        var logs = OtlpSignalSettings.Read(builder.Configuration, "logs");
        var traces = OtlpSignalSettings.Read(builder.Configuration, "traces");
        var metrics = OtlpSignalSettings.Read(builder.Configuration, "metrics");
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => { o.FormatterName = "safe-json"; o.LogToStandardErrorThreshold = LogLevel.None; })
            .AddConsoleFormatter<SafeJsonConsoleFormatter, ConsoleFormatterOptions>();
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.SetResourceBuilder(resource);
            o.IncludeFormattedMessage = false;
            o.ParseStateValues = true;
            // Arbitrary scopes can contain credentials. Correlation is copied explicitly by the processor.
            o.IncludeScopes = false;
            o.AddProcessor(new SafeLogProcessor());
            if (logs is not null) o.AddOtlpExporter("logs", logs.Apply);
        });
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.Clear().AddAttributes(resourceAttributes))
            .WithMetrics(m => m.AddMeter("CleanArchitecture.Application", "CleanArchitecture.Integrations")
                .AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation()
                .ConfigureMetrics(metrics))
            .WithTracing(t => t.AddSource(builder.Environment.ApplicationName, "CleanArchitecture.Integrations")
                .AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().ConfigureTraces(traces));
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());
        // Resilience is installed only by Infrastructure for explicitly classified operations.
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }
    private static MeterProviderBuilder ConfigureMetrics(this MeterProviderBuilder builder, OtlpSignalSettings? settings)
        => settings is null ? builder : builder.AddOtlpExporter("metrics", settings.Apply);
    private static TracerProviderBuilder ConfigureTraces(this TracerProviderBuilder builder, OtlpSignalSettings? settings)
        => settings is null ? builder : builder.AddOtlpExporter("traces", settings.Apply);
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
