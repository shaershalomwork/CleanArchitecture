using CleanArchitecture.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Oracle.ManagedDataAccess.Client;

namespace CleanArchitecture.Infrastructure.Connections;

public sealed class OracleConnectionFactory(IConfiguration configuration, ICorrelationContext correlation)
{
    public async Task<OracleConnection> OpenAsync(string connectionName, CancellationToken cancellationToken)
    {
        var value = configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException("The named source connection is not configured.");
        var connection = new OracleConnection(value) { BindByName = true };
        try
        {
            await connection.OpenAsync(cancellationToken);
            connection.ClientId = correlation.Id;
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
        // ODP.NET Core clears ClientId on Close/Dispose before pooling the connection.
    }
}
