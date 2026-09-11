using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Connections;
using CleanArchitecture.Infrastructure.Execution;
using Dapper;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Sources.CustomerRegistry;

public sealed class SqliteCustomerSourceAdapter(SqliteConnectionFactory connections, SourceExecutor executor,
    IOptions<CustomerRegistryOptions> options, ICorrelationContext correlation, TimeProvider clock) : ICustomerSourceAdapter
{
    public Task<OperationResult<IReadOnlyList<Customer>>> GetCustomersAsync(CancellationToken cancellationToken) =>
        executor.ExecuteAsync(new("CUSTOMER", "GetCustomers", IsReadOnly: true), token => Task.Run(() =>
        {
            using var connection = connections.Open(options.Value.ConnectionName, token);
            var rows = connection.Query<CustomerRow>("SELECT Id, DisplayName FROM Customers",
                commandTimeout: connection.DefaultTimeout);
            return CustomerReadResults.FromRows(rows.Select(x => (x.Id, x.DisplayName)), clock.GetUtcNow(), token);
        }, token), cancellationToken);

    public Task<OperationResult<Customer>> GetCustomerAsync(string customerId, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(new("CUSTOMER", "GetCustomer", IsReadOnly: true), token => Task.Run(() =>
        {
            // Sqlite has synchronous I/O; isolate the bounded point lookup from workflow dispatch.
            using var connection = connections.Open(options.Value.ConnectionName, token);
            var rows = connection.Query<CustomerRow>(
                "SELECT Id, DisplayName FROM Customers WHERE Id = @CustomerId LIMIT 2",
                new { CustomerId = customerId }, commandTimeout: connection.DefaultTimeout).ToArray();
            token.ThrowIfCancellationRequested();
            if (rows.Length == 0)
                return OperationResult<Customer>.Failure(new OperationIssue("CUSTOMER.NOT_FOUND",
                    "The requested customer was not found.", IssueCategory.Business, correlation.Id));
            if (rows.Length != 1 || rows[0].Id != customerId || string.IsNullOrWhiteSpace(rows[0].DisplayName))
                throw new SourceFailureException("INVALID_RESPONSE");
            return OperationResult<Customer>.Success(new(rows[0].Id, rows[0].DisplayName, clock.GetUtcNow()));
        }, token), cancellationToken);

    private sealed class CustomerRow
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }
}
