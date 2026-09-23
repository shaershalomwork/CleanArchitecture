using System.Data;
using CleanArchitecture.Application.Common.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CleanArchitecture.Infrastructure.Connections;

public sealed class SqlConnectionFactory(IConfiguration configuration, ICorrelationContext correlation)
{
    public async Task<SqlConnectionLease> OpenAsync(string connectionName, CancellationToken cancellationToken)
    {
        var value = configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException("The named source connection is not configured.");
        var settings = new SqlConnectionStringBuilder(value) { ConnectRetryCount = 0 };
        var connection = new SqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                "sys.sp_set_session_context", new { key = "CorrelationId", value = correlation.Id },
                commandType: CommandType.StoredProcedure, commandTimeout: 3, cancellationToken: cancellationToken));
            return new SqlConnectionLease(connection);
        }
        catch { SqlConnection.ClearPool(connection); await connection.DisposeAsync(); throw; }
    }
}
public sealed class SqlConnectionLease(SqlConnection connection) : IAsyncDisposable
{
    public SqlConnection Connection { get; } = connection;
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Connection.State == ConnectionState.Open)
                await Connection.ExecuteAsync(new CommandDefinition(
                    "sys.sp_set_session_context", new { key = "CorrelationId", value = (string?)null },
                    commandType: CommandType.StoredProcedure, commandTimeout: 1));
        }
        catch { SqlConnection.ClearPool(Connection); }
        finally { await Connection.DisposeAsync(); }
    }
}
