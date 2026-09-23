using System.Diagnostics;
using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace CleanArchitecture.Infrastructure.Execution;

public sealed class SourceExecutor(SourcePipelines pipelines, ICorrelationContext correlation, ILogger<SourceExecutor> logger)
{
    public async Task<OperationResult<T>> ExecuteAsync<T>(SourceOperation operation,
        Func<CancellationToken, Task<OperationResult<T>>> action, CancellationToken cancellationToken)
    {
        using var activity = IntegrationTelemetry.Activities.StartActivity(operation.Source + "." + operation.Name, ActivityKind.Client);
        activity?.SetTag("source", operation.Source);
        activity?.SetTag("operation", operation.Name);
        var started = Stopwatch.GetTimestamp();
        var outcome = "Cancelled";
        try
        {
            var result = await pipelines.Get(operation).ExecuteAsync(async token =>
            {
                IntegrationTelemetry.Attempts.Add(1, new("source", operation.Source), new("operation", operation.Name));
                try { return await action(token); }
                catch (HttpRequestException) { throw new SourceFailureException("UNAVAILABLE", true); }
                catch (JsonException) { throw new SourceFailureException("INVALID_RESPONSE"); }
            }, cancellationToken);
            outcome = result.Status.ToString();
            if (result.Status != OperationStatus.Success) activity?.SetStatus(ActivityStatusCode.Error);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is SourceFailureException or TimeoutRejectedException or BrokenCircuitException)
        {
            var suffix = e switch
            {
                TimeoutRejectedException => "TIMEOUT",
                BrokenCircuitException => "CIRCUIT_OPEN",
                SourceFailureException failure => failure.Suffix,
                _ => "UNAVAILABLE"
            };
            // A timed-out/disconnected write might have committed. Never imply rollback.
            if (!operation.IsReadOnly && suffix is "TIMEOUT" or "UNAVAILABLE")
                suffix = "OUTCOME_UNKNOWN";
            outcome = "Error";
            activity?.SetStatus(ActivityStatusCode.Error, suffix);
            logger.LogWarning("Source {Source} operation {Operation} failed with {Code}; correlation {CorrelationId}",
                operation.Source, operation.Name, suffix, correlation.Id);
            return OperationResult<T>.Failure(new OperationIssue(operation.Source + "." + suffix,
                suffix == "OUTCOME_UNKNOWN" ? "The write outcome is unknown. Reconcile before retrying." : "The upstream operation could not be completed.",
                IssueCategory.Technical, correlation.Id));
        }
        catch
        {
            outcome = "Error";
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            IntegrationTelemetry.Latency.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new("source", operation.Source), new("operation", operation.Name), new("outcome", outcome));
            IntegrationTelemetry.Results.Add(1, new("source", operation.Source), new("operation", operation.Name), new("outcome", outcome));
        }
    }
}
