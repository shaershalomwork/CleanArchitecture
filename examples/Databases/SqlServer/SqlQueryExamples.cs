using System.Data;
using CleanArchitecture.Infrastructure.Execution;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CleanArchitecture.Infrastructure.Sources.CustomerRegistry;

// Executable examples of database calls; registered adapters also map results and errors.
public static class SqlQueryExamples
{
    public sealed class CustomerRow
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    public static Task<CustomerRow?> FindWithQueryAsync(SqlConnection connection, string customerId, CancellationToken token) =>
        connection.QuerySingleOrDefaultAsync<CustomerRow>(new CommandDefinition(
            "SELECT Id, DisplayName FROM dbo.Customers WHERE Id = @CustomerId",
            new { CustomerId = customerId }, commandTimeout: 3, cancellationToken: token));

    public static async Task<CustomerRow?> FindWithProcedureAsync(SqlConnection connection, string customerId, CancellationToken token)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@CustomerId", customerId, DbType.String, size: 50);
        parameters.Add("@ReturnCode", dbType: DbType.Int32, direction: ParameterDirection.ReturnValue);
        var row = await connection.QuerySingleOrDefaultAsync<CustomerRow>(new CommandDefinition(
            "dbo.GetCustomer", parameters, commandType: CommandType.StoredProcedure,
            commandTimeout: 3, cancellationToken: token));
        var code = parameters.Get<int>("@ReturnCode");
        if (code == 404 && row is null) return null;
        if (code != 0 || row is null || row.Id != customerId || string.IsNullOrWhiteSpace(row.DisplayName))
            throw new SourceFailureException("INVALID_RESPONSE");
        return row;
    }
}
