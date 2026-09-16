using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Connections;
using CleanArchitecture.Infrastructure.Execution;
using Dapper;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;

namespace CleanArchitecture.Infrastructure.Sources.CustomerRegistry;

public sealed class OracleCustomerWriteSourceAdapter(OracleConnectionFactory connections, SourceExecutor executor,
    IOptions<CustomerRegistryOptions> options, IOptions<SourceExecutionOptions> execution,
    ICorrelationContext correlation, TimeProvider clock) : ICustomerWriteSourceAdapter
{
    public Task<OperationResult<Customer>> CreateAsync(string customerId, string displayName, CancellationToken cancellationToken) =>
        SaveAsync("CreateCustomer", true, customerId, displayName, cancellationToken);
    public Task<OperationResult<Customer>> ReplaceAsync(string customerId, string displayName, CancellationToken cancellationToken) =>
        SaveAsync("ReplaceCustomer", false, customerId, displayName, cancellationToken);
    public Task<OperationResult<Customer>> PatchAsync(string customerId, string displayName, CancellationToken cancellationToken) =>
        SaveAsync("PatchCustomer", false, customerId, displayName, cancellationToken);

    private Task<OperationResult<Customer>> SaveAsync(string operation, bool create, string customerId, string displayName, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(new("CUSTOMER", operation), async token =>
        {
            await using var connection = await connections.OpenAsync(options.Value.ConnectionName, token);
            await using var transaction = await connection.BeginTransactionAsync(token);
            int affected;
            try
            {
                affected = await connection.ExecuteAsync(new CommandDefinition(create
                    ? "INSERT INTO Customers (Id, DisplayName) VALUES (:CustomerId, :DisplayName)"
                    : "UPDATE Customers SET DisplayName = :DisplayName WHERE Id = :CustomerId",
                    new { CustomerId = customerId, DisplayName = displayName }, transaction,
                    commandTimeout: (int)Math.Ceiling(execution.Value.AttemptTimeoutSeconds), cancellationToken: token));
            }
            catch (OracleException exception) when (create && exception.Number == 1)
            {
                return CustomerWriteResults.Conflict<Customer>(correlation.Id);
            }
            if (affected == 0 && !create) return CustomerWriteResults.NotFound<Customer>(correlation.Id);
            if (affected != 1) throw new SourceFailureException("INVALID_RESPONSE");
            await transaction.CommitAsync(token);
            return OperationResult<Customer>.Success(new(customerId, displayName, clock.GetUtcNow()));
        }, cancellationToken);

    public Task<OperationResult<NoData>> DeleteAsync(string customerId, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(new("CUSTOMER", "DeleteCustomer"), async token =>
        {
            await using var connection = await connections.OpenAsync(options.Value.ConnectionName, token);
            await using var transaction = await connection.BeginTransactionAsync(token);
            var affected = await connection.ExecuteAsync(new CommandDefinition("DELETE FROM Customers WHERE Id = :CustomerId",
                new { CustomerId = customerId }, transaction,
                commandTimeout: (int)Math.Ceiling(execution.Value.AttemptTimeoutSeconds), cancellationToken: token));
            if (affected == 0) return CustomerWriteResults.NotFound<NoData>(correlation.Id);
            if (affected != 1) throw new SourceFailureException("INVALID_RESPONSE");
            await transaction.CommitAsync(token);
            return OperationResult<NoData>.Success(new());
        }, cancellationToken);
}
