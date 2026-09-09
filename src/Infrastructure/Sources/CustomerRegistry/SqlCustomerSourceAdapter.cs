using System.Data;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Connections;
using CleanArchitecture.Infrastructure.Execution;
using Dapper;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Sources.CustomerRegistry;

public sealed class SqlCustomerSourceAdapter(SqlConnectionFactory connections, SourceExecutor executor,
    IOptions<CustomerRegistryOptions> options, IOptions<SourceExecutionOptions> execution, ICorrelationContext correlation, TimeProvider clock)
    : ICustomerSourceAdapter
{
    public Task<OperationResult<Customer>> GetCustomerAsync(string customerId, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(new("CUSTOMER", "GetCustomer", IsReadOnly: true), async token =>
        {
            await using var lease = await connections.OpenAsync(options.Value.ConnectionName, token);
            var parameters = new DynamicParameters();
            parameters.Add("@CustomerId", customerId, DbType.String, size: 50);
            parameters.Add("@ReturnCode", dbType: DbType.Int32, direction: ParameterDirection.ReturnValue);
            var row = await lease.Connection.QuerySingleOrDefaultAsync<CustomerRow>(new CommandDefinition(
                "dbo.GetCustomer", parameters, commandType: CommandType.StoredProcedure,
                commandTimeout: (int)Math.Ceiling(execution.Value.AttemptTimeoutSeconds), cancellationToken: token));
            var returnCode = parameters.Get<int>("@ReturnCode");
            if (returnCode == 404 && row is null)
                return OperationResult<Customer>.Failure(new OperationIssue("CUSTOMER.NOT_FOUND", "The requested customer was not found.", IssueCategory.Business, correlation.Id));
            if (returnCode != 0 || row is null || row.Id != customerId || string.IsNullOrWhiteSpace(row.DisplayName))
                throw new SourceFailureException("INVALID_RESPONSE");
            return OperationResult<Customer>.Success(new(row.Id, row.DisplayName, clock.GetUtcNow()));
        }, cancellationToken);

    private sealed class CustomerRow
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }
}
