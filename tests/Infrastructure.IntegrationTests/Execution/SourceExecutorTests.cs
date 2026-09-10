using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Results;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
namespace CleanArchitecture.Infrastructure.IntegrationTests.Execution;
public class SourceExecutorTests
{
    [TestCase(1013, "TIMEOUT", true)]
    [TestCase(1017, "AUTHENTICATION_FAILED", false)]
    [TestCase(1031, "AUTHENTICATION_FAILED", false)]
    [TestCase(3113, "UNAVAILABLE", true)]
    [TestCase(12541, "UNAVAILABLE", true)]
    [TestCase(12154, "UNAVAILABLE", false)]
    [TestCase(942, "INVALID_RESPONSE", false)]
    public async Task OracleErrorsUseSafeCodesAndNeverRetryWrites(int number, string suffix, bool transient)
    {
        var classified = SourceExecutor.ClassifyOracle(number);
        classified.Suffix.ShouldBe(suffix);
        classified.Transient.ShouldBe(transient);
        var calls = 0;
        var result = await Create().ExecuteAsync<int>(new("CUSTOMER", "WriteOracle"), _ =>
        {
            calls++;
            throw classified;
        }, default);
        calls.ShouldBe(1);
        result.Issues[0].Code.ShouldBe("CUSTOMER." + (suffix is "TIMEOUT" or "UNAVAILABLE" ? "OUTCOME_UNKNOWN" : suffix));
        result.Issues[0].Message.ShouldNotContain("ORA-");
    }
    internal sealed class Correlation : ICorrelationContext { public string Id => "test-trace"; }
    internal static SourceExecutor Create(SourceExecutionOptions? options = null) =>
        new(new SourcePipelines(Options.Create(options ?? new() { RetryDelayMilliseconds = 0 })),
            new Correlation(), NullLogger<SourceExecutor>.Instance);

    [TestCase(true, 2, "SOURCE.UNAVAILABLE")]
    [TestCase(false, 1, "SOURCE.OUTCOME_UNKNOWN")]
    public async Task RetriesOnlyReads(bool read, int expectedAttempts, string code)
    {
        var calls = 0;
        var result = await Create().ExecuteAsync<int>(new("SOURCE", "Test", read), _ =>
        {
            calls++;
            throw new SourceFailureException("UNAVAILABLE", true);
        }, default);
        calls.ShouldBe(expectedAttempts);
        result.Issues[0].Code.ShouldBe(code);
    }
    [Test] public async Task BusinessRejectionsAreNotRetried()
    {
        var calls = 0;
        await Create().ExecuteAsync(new("SOURCE", "Read", true), _ =>
        {
            calls++;
            return Task.FromResult(OperationResult<int>.Failure(new OperationIssue("SOURCE.REJECTED", "Rejected", IssueCategory.Business, "trace")));
        }, default);
        calls.ShouldBe(1);
    }
    [Test] public async Task WriteTimeoutIsNeverRetried()
    {
        var calls = 0;
        var executor = Create(new() { AttemptTimeoutSeconds = 0.1, TotalTimeoutSeconds = 1 });
        var result = await executor.ExecuteAsync<int>(new("SOURCE", "Write"), async ct =>
        {
            calls++;
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return OperationResult<int>.Success(1);
        }, default);
        calls.ShouldBe(1);
        result.Issues[0].Code.ShouldBe("SOURCE.OUTCOME_UNKNOWN");
    }
    [Test] public void CallerCancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Should.ThrowAsync<OperationCanceledException>(() => Create().ExecuteAsync(new("SOURCE", "Read", true),
            ct => { ct.ThrowIfCancellationRequested(); return Task.FromResult(OperationResult<int>.Success(1)); }, cts.Token));
    }
    [Test] public async Task CircuitStateIsIsolatedByOperation()
    {
        var executor = Create(new() { MinimumThroughput = 2, ReadRetries = 0 });
        for (var i = 0; i < 2; i++)
            await executor.ExecuteAsync<int>(new("SOURCE", "Broken", true), _ => throw new SourceFailureException("UNAVAILABLE", true), default);
        var blocked = await executor.ExecuteAsync(new("SOURCE", "Broken", true),
            _ => Task.FromResult(OperationResult<int>.Success(1)), default);
        blocked.Issues[0].Code.ShouldBe("SOURCE.CIRCUIT_OPEN");
        var healthy = await executor.ExecuteAsync(new("SOURCE", "Healthy", true),
            _ => Task.FromResult(OperationResult<int>.Success(1)), default);
        healthy.Status.ShouldBe(OperationStatus.Success);
    }
}
