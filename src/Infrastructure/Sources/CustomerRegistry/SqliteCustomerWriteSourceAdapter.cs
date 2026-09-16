using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Connections;
using CleanArchitecture.Infrastructure.Execution;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Sources.CustomerRegistry;

public sealed class SqliteCustomerWriteSourceAdapter(SqliteWriteConnectionFactory connections, SourceExecutor executor,
    IOptions<CustomerRegistryOptions> options, ICorrelationContext correlation, TimeProvider clock) : ICustomerWriteSourceAdapter
{
    public Task<OperationResult<Customer>> CreateAsync(string customerId, string displayName, CancellationToken cancellationToken) =>
        SaveAsync("CreateCustomer", true, customerId, displayName, cancellationToken);
    public Task<OperationResult<Customer>> ReplaceAsync(string customerId, string displayName, CancellationToken cancellationToken) =>
        SaveAsync("ReplaceCustomer", false, customerId, displayName, cancellationToken);
    public Task<OperationResult<Customer>> PatchAsync(string customerId, string displayName, CancellationToken cancellationToken) =>
        SaveAsync("PatchCustomer", false, customerId, displayName, cancellationToken);

    private Task<OperationResult<Customer>> SaveAsync(string operation, bool create, string customerId, string displayName, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(new("CUSTOMER", operation), token => Task.Run(() =>
        {
            using var connection = connections.Open(options.Value.ConnectionName, token);
            using var transaction = connection.BeginTransaction();
            int affected;
            try
            {
                affected = connection.Execute(create
                    ? "INSERT INTO Customers (Id, DisplayName) VALUES (@CustomerId, @DisplayName)"
                    : "UPDATE Customers SET DisplayName = @DisplayName WHERE Id = @CustomerId",
                    new { CustomerId = customerId, DisplayName = displayName }, transaction, connection.DefaultTimeout);
            }
            catch (SqliteException exception) when (create && exception.SqliteExtendedErrorCode is 1555 or 2067)
            {
                return CustomerWriteResults.Conflict<Customer>(correlation.Id);
            }
            token.ThrowIfCancellationRequested();
            if (affected == 0 && !create) return CustomerWriteResults.NotFound<Customer>(correlation.Id);
            if (affected != 1) throw new SourceFailureException("INVALID_RESPONSE");
            transaction.Commit();
            token.ThrowIfCancellationRequested();
            return OperationResult<Customer>.Success(new(customerId, displayName, clock.GetUtcNow()));
        }, token), cancellationToken);

    public Task<OperationResult<NoData>> DeleteAsync(string customerId, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(new("CUSTOMER", "DeleteCustomer"), token => Task.Run(() =>
        {
            using var connection = connections.Open(options.Value.ConnectionName, token);
            using var transaction = connection.BeginTransaction();
            var affected = connection.Execute("DELETE FROM Customers WHERE Id = @CustomerId", new { CustomerId = customerId },
                transaction, connection.DefaultTimeout);
            token.ThrowIfCancellationRequested();
            if (affected == 0) return CustomerWriteResults.NotFound<NoData>(correlation.Id);
            if (affected != 1) throw new SourceFailureException("INVALID_RESPONSE");
            transaction.Commit();
            token.ThrowIfCancellationRequested();
            return OperationResult<NoData>.Success(new());
        }, token), cancellationToken);
}
