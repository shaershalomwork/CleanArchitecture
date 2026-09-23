using Oracle.ManagedDataAccess.Client;

namespace CleanArchitecture.Infrastructure.Execution;

// Classify inside each attempt so the shared executor retains its read retry/write outcome policy.
public static class OracleExecution
{
    public static Task<OperationResult<T>> ExecuteOracleAsync<T>(this SourceExecutor executor,
        SourceOperation operation, Func<CancellationToken, Task<OperationResult<T>>> action, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(operation, async token =>
        {
            try { return await action(token); }
            catch (OracleException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
            catch (OracleException exception) { throw Classify(exception.Number); }
        }, cancellationToken);

    internal static SourceFailureException Classify(int number) => number switch
    {
        1013 => new("TIMEOUT", true),
        1017 or 1031 or 28000 or 28001 => new("AUTHENTICATION_FAILED"),
        3113 or 3114 or 3135 or 12170 or 12514 or 12541 or 12537 or 12545 or 12570 => new("UNAVAILABLE", true),
        12154 => new("UNAVAILABLE"),
        _ => new("INVALID_RESPONSE")
    };
}
