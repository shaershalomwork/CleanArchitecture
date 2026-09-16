using System.Diagnostics;
using System.Diagnostics.Metrics;
using CleanArchitecture.Application.Common.Interfaces;
namespace CleanArchitecture.Infrastructure.Observability;

public sealed class CorrelationContext : ICorrelationContext
{
    private readonly string _fallback = ActivityTraceId.CreateRandom().ToString();
    public string Id => Activity.Current?.TraceId.ToString() ?? _fallback;
}
public static class IntegrationTelemetry
{
    public const string Name = "CleanArchitecture.Integrations";
    public static readonly ActivitySource Activities = new(Name);
    private static readonly Meter Meter = new(Name);
    public static readonly Histogram<double> Latency = Meter.CreateHistogram<double>("source.duration", "ms");
    public static readonly Counter<long> Attempts = Meter.CreateCounter<long>("source.attempts");
    public static readonly Counter<long> Results = Meter.CreateCounter<long>("source.results");
    public static readonly Counter<long> CircuitTransitions = Meter.CreateCounter<long>("source.circuit.transitions");
}
