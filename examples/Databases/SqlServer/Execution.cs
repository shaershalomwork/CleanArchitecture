using Microsoft.Data.SqlClient;

namespace CleanArchitecture.Infrastructure.Execution;

// Classify inside each attempt so the shared executor retains its read retry/write outcome policy.
public static class SqlServerExecution
{
    public static Task<OperationResult<T>> ExecuteSqlServerAsync<T>(this SourceExecutor executor,
        SourceOperation operation, Func<CancellationToken, Task<OperationResult<T>>> action, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(operation, async token =>
        {
            try { return await action(token); }
            catch (SqlException exception) { throw Classify(exception.Number); }
        }, cancellationToken);

    internal static SourceFailureException Classify(int number) => number switch
    {
        -2 => new("TIMEOUT", true),
        18456 => new("AUTHENTICATION_FAILED"),
        53 or 64 or 233 or 10053 or 10054 or 10060 or 40613 or 40197 or 40501 or 1205 => new("UNAVAILABLE", true),
        _ => new("INVALID_RESPONSE")
    };
}
