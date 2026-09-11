using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Connections;
using CleanArchitecture.Infrastructure.Execution;
using Dapper;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Sources.CustomerRegistry;

public sealed class OracleCustomerSourceAdapter(OracleConnectionFactory connections, SourceExecutor executor,
    IOptions<CustomerRegistryOptions> options, IOptions<SourceExecutionOptions> execution,
    ICorrelationContext correlation, TimeProvider clock) : ICustomerSourceAdapter
{
    public Task<OperationResult<IReadOnlyList<Customer>>> GetCustomersAsync(CancellationToken cancellationToken) =>
        executor.ExecuteAsync(new("CUSTOMER", "GetCustomers", IsReadOnly: true), async token =>
        {
            await using var connection = await connections.OpenAsync(options.Value.ConnectionName, token);
            var rows = await connection.QueryAsync<CustomerRow>(new CommandDefinition(
                "SELECT Id, DisplayName FROM Customers",
                commandTimeout: (int)Math.Ceiling(execution.Value.AttemptTimeoutSeconds), cancellationToken: token));
            return CustomerReadResults.FromRows(rows.Select(x => (x.Id, x.DisplayName)), clock.GetUtcNow(), token);
        }, cancellationToken);

    public Task<OperationResult<Customer>> GetCustomerAsync(string customerId, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(new("CUSTOMER", "GetCustomer", IsReadOnly: true), async token =>
        {
            await using var connection = await connections.OpenAsync(options.Value.ConnectionName, token);
            var rows = (await connection.QueryAsync<CustomerRow>(new CommandDefinition(
                "SELECT Id, DisplayName FROM Customers WHERE Id = :CustomerId FETCH FIRST 2 ROWS ONLY",
                new { CustomerId = customerId }, commandTimeout: (int)Math.Ceiling(execution.Value.AttemptTimeoutSeconds),
                cancellationToken: token))).ToArray();
            if (rows.Length == 0) return CustomerWriteResults.NotFound<Customer>(correlation.Id);
            if (rows.Length != 1 || rows[0].Id != customerId || string.IsNullOrWhiteSpace(rows[0].DisplayName))
                throw new SourceFailureException("INVALID_RESPONSE");
            return OperationResult<Customer>.Success(new(rows[0].Id, rows[0].DisplayName, clock.GetUtcNow()));
        }, cancellationToken);

    private sealed class CustomerRow
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }
}
