using System.Diagnostics;
using System.Diagnostics.Metrics;
using CleanArchitecture.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Application.Common.Behaviours;
public sealed class LoggingBehaviour<TRequest, TResponse>(
    ILogger<LoggingBehaviour<TRequest, TResponse>> logger, ICorrelationContext correlation)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull where TResponse : IOperationResult
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "Cancelled";
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlation.Id });
        try
        {
            var response = await next(cancellationToken);
            outcome = response.Status.ToString();
            return response;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            UseCaseTelemetry.Latency.Record(elapsed, new("use_case", typeof(TRequest).Name), new("outcome", outcome));
            logger.LogInformation("Use case {UseCase} completed with {Outcome} in {ElapsedMs} ms", typeof(TRequest).Name, outcome, elapsed);
        }
    }
}
public static class UseCaseTelemetry
{
    public const string Name = "CleanArchitecture.Application";
    private static readonly Meter Meter = new(Name);
    public static readonly Histogram<double> Latency = Meter.CreateHistogram<double>("use_case.duration", "ms");
}
