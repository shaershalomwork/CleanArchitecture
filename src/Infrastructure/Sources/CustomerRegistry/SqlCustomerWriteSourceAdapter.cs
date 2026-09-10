using System.Data;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Connections;
using CleanArchitecture.Infrastructure.Execution;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Sources.CustomerRegistry;

public sealed class SqlCustomerWriteSourceAdapter(SqlConnectionFactory connections, SourceExecutor executor,
    IOptions<CustomerRegistryOptions> options, IOptions<SourceExecutionOptions> execution,
    ICorrelationContext correlation, TimeProvider clock) : ICustomerWriteSourceAdapter
{
    public Task<OperationResult<Customer>> CreateAsync(string customerId, string displayName, CancellationToken cancellationToken) =>
        SaveAsync("CreateCustomer", "dbo.CreateCustomer", customerId, displayName, cancellationToken);
    public Task<OperationResult<Customer>> ReplaceAsync(string customerId, string displayName, CancellationToken cancellationToken) =>
        SaveAsync("ReplaceCustomer", "dbo.UpdateCustomer", customerId, displayName, cancellationToken);
    public Task<OperationResult<Customer>> PatchAsync(string customerId, string displayName, CancellationToken cancellationToken) =>
        SaveAsync("PatchCustomer", "dbo.UpdateCustomer", customerId, displayName, cancellationToken);

    private Task<OperationResult<Customer>> SaveAsync(string operation, string procedure, string customerId, string displayName, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(new("CUSTOMER", operation), async token =>
        {
            await using var lease = await connections.OpenAsync(options.Value.ConnectionName, token);
            await using var transaction = await lease.Connection.BeginTransactionAsync(token);
            var parameters = Parameters(customerId);
            parameters.Add("@DisplayName", displayName, DbType.String, size: 200);
            try
            {
                await lease.Connection.ExecuteAsync(new CommandDefinition(procedure, parameters, transaction,
                    commandTimeout: (int)Math.Ceiling(execution.Value.AttemptTimeoutSeconds),
                    commandType: CommandType.StoredProcedure, cancellationToken: token));
            }
            catch (SqlException exception) when (operation == "CreateCustomer" && exception.Number is 2601 or 2627)
            {
                return CustomerWriteResults.Conflict<Customer>(correlation.Id);
            }
            var code = parameters.Get<int>("@ReturnCode");
            if (code == 404 && operation != "CreateCustomer") return CustomerWriteResults.NotFound<Customer>(correlation.Id);
            if (code != 0) throw new SourceFailureException("INVALID_RESPONSE");
            await transaction.CommitAsync(token);
            return OperationResult<Customer>.Success(new(customerId, displayName, clock.GetUtcNow()));
        }, cancellationToken);

    public Task<OperationResult<NoData>> DeleteAsync(string customerId, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(new("CUSTOMER", "DeleteCustomer"), async token =>
        {
            await using var lease = await connections.OpenAsync(options.Value.ConnectionName, token);
            await using var transaction = await lease.Connection.BeginTransactionAsync(token);
            var parameters = Parameters(customerId);
            await lease.Connection.ExecuteAsync(new CommandDefinition("dbo.DeleteCustomer", parameters, transaction,
                commandTimeout: (int)Math.Ceiling(execution.Value.AttemptTimeoutSeconds),
                commandType: CommandType.StoredProcedure, cancellationToken: token));
            var code = parameters.Get<int>("@ReturnCode");
            if (code == 404) return CustomerWriteResults.NotFound<NoData>(correlation.Id);
            if (code != 0) throw new SourceFailureException("INVALID_RESPONSE");
            await transaction.CommitAsync(token);
            return OperationResult<NoData>.Success(new());
        }, cancellationToken);

    private static DynamicParameters Parameters(string customerId)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@CustomerId", customerId, DbType.String, size: 50);
        parameters.Add("@ReturnCode", dbType: DbType.Int32, direction: ParameterDirection.ReturnValue);
        return parameters;
    }
}
