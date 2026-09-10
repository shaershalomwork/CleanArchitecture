using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Execution;

namespace CleanArchitecture.Infrastructure.Fakes;

public sealed class FakeCustomerWriteSourceAdapter(FakeCustomerStore store, SourceExecutor executor,
    ICorrelationContext correlation, TimeProvider clock) : ICustomerWriteSourceAdapter
{
    public Task<OperationResult<Customer>> CreateAsync(string customerId, string displayName, CancellationToken cancellationToken) =>
        SaveAsync("CreateCustomer", true, customerId, displayName, cancellationToken);
    public Task<OperationResult<Customer>> ReplaceAsync(string customerId, string displayName, CancellationToken cancellationToken) =>
        SaveAsync("ReplaceCustomer", false, customerId, displayName, cancellationToken);
    public Task<OperationResult<Customer>> PatchAsync(string customerId, string displayName, CancellationToken cancellationToken) =>
        SaveAsync("PatchCustomer", false, customerId, displayName, cancellationToken);

    private Task<OperationResult<Customer>> SaveAsync(string operation, bool create, string id, string name, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(new("CUSTOMER", operation), token =>
        {
            token.ThrowIfCancellationRequested();
            if (id == "CUST-FAIL") throw new SourceFailureException("UNAVAILABLE", true);
            return Task.FromResult(store.Save(create, id, name, clock.GetUtcNow(), correlation.Id));
        }, cancellationToken);

    public Task<OperationResult<NoData>> DeleteAsync(string customerId, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(new("CUSTOMER", "DeleteCustomer"), token =>
        {
            token.ThrowIfCancellationRequested();
            if (customerId == "CUST-FAIL") throw new SourceFailureException("UNAVAILABLE", true);
            return Task.FromResult(store.Delete(customerId, correlation.Id));
        }, cancellationToken);
}
