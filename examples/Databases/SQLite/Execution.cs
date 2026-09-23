using Microsoft.Data.Sqlite;

namespace CleanArchitecture.Infrastructure.Execution;

// Classify inside each attempt so the shared executor retains its read retry/write outcome policy.
public static class SQLiteExecution
{
    public static Task<OperationResult<T>> ExecuteSQLiteAsync<T>(this SourceExecutor executor,
        SourceOperation operation, Func<CancellationToken, Task<OperationResult<T>>> action, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(operation, async token =>
        {
            try { return await action(token); }
            catch (SqliteException exception) { throw Classify(exception.SqliteErrorCode); }
        }, cancellationToken);

    internal static SourceFailureException Classify(int number) => number switch
    {
        5 or 6 => new("TIMEOUT", true),
        3 or 23 => new("AUTHENTICATION_FAILED"),
        10 => new("UNAVAILABLE", true),
        14 => new("UNAVAILABLE"),
        _ => new("INVALID_RESPONSE")
    };
}
