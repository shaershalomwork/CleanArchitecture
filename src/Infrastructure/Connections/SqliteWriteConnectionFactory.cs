using CleanArchitecture.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Connections;

public sealed class SqliteWriteConnectionFactory(IConfiguration configuration, IOptions<SourceExecutionOptions> execution)
{
    public SqliteConnection Open(string connectionName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException("The named source connection is not configured.");
        var settings = new SqliteConnectionStringBuilder(value)
        {
            Mode = SqliteOpenMode.ReadWrite, Pooling = true,
            DefaultTimeout = Math.Max(1, (int)Math.Ceiling(execution.Value.AttemptTimeoutSeconds))
        };
        var connection = new SqliteConnection(settings.ConnectionString);
        try
        {
            connection.Open();
            cancellationToken.ThrowIfCancellationRequested();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }
}
