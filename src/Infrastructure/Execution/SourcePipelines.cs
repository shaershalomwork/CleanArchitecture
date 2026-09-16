using System.Collections.Concurrent;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Observability;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace CleanArchitecture.Infrastructure.Execution;

public sealed record SourceOperation(string Source, string Name, bool IsReadOnly = false);
public sealed class SourceFailureException(string suffix, bool transient = false) : Exception("A source operation failed.")
{
    public string Suffix { get; } = suffix;
    public bool Transient { get; } = transient;
}
public sealed class SourcePipelines(IOptions<SourceExecutionOptions> options)
{
    private readonly ConcurrentDictionary<SourceOperation, Lazy<ResiliencePipeline>> _pipelines = new();
    public ResiliencePipeline Get(SourceOperation operation) =>
        _pipelines.GetOrAdd(operation, key => new(() => Build(key))).Value;

    private ResiliencePipeline Build(SourceOperation operation)
    {
        var o = options.Value;
        var builder = new ResiliencePipelineBuilder()
            .AddTimeout(TimeSpan.FromSeconds(o.TotalTimeoutSeconds));
        var predicate = new PredicateBuilder().Handle<SourceFailureException>(e => e.Transient).Handle<TimeoutRejectedException>();
        if (operation.IsReadOnly && o.ReadRetries > 0)
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = o.ReadRetries, Delay = TimeSpan.FromMilliseconds(o.RetryDelayMilliseconds),
                BackoffType = DelayBackoffType.Exponential, UseJitter = true, ShouldHandle = predicate
            });
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = o.FailureRatio, MinimumThroughput = o.MinimumThroughput,
            SamplingDuration = TimeSpan.FromSeconds(o.SamplingSeconds), BreakDuration = TimeSpan.FromSeconds(o.BreakSeconds),
            ShouldHandle = predicate,
            OnOpened = _ => { Record("open"); return default; },
            OnClosed = _ => { Record("closed"); return default; },
            OnHalfOpened = _ => { Record("half_open"); return default; }
        });
        return builder.AddTimeout(TimeSpan.FromSeconds(o.AttemptTimeoutSeconds)).Build();

        void Record(string state) => IntegrationTelemetry.CircuitTransitions.Add(1,
            new("source", operation.Source), new("operation", operation.Name), new("state", state));
    }
}
