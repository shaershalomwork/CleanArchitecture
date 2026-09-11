using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Execution;

namespace CleanArchitecture.Infrastructure.Fakes;

public sealed class FakeCustomerSourceAdapter(ICorrelationContext correlation, TimeProvider clock, SourceExecutor executor, FakeCustomerStore store) : ICustomerSourceAdapter
{
    public Task<OperationResult<IReadOnlyList<Customer>>> GetCustomersAsync(CancellationToken cancellationToken) =>
        executor.ExecuteAsync(new("CUSTOMER", "GetCustomers", IsReadOnly: true), token =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(store.ReadAll(clock.GetUtcNow()));
        }, cancellationToken);

    public Task<OperationResult<Customer>> GetCustomerAsync(string customerId, CancellationToken cancellationToken)
        => executor.ExecuteAsync(new("CUSTOMER", "GetCustomer", IsReadOnly: true), token =>
    {
        token.ThrowIfCancellationRequested();
        var result = customerId switch
        {
            "CUST-MISSING" => OperationResult<Customer>.Failure(new OperationIssue("CUSTOMER.NOT_FOUND", "The requested customer was not found.", IssueCategory.Business, correlation.Id)),
            "CUST-FAIL" => OperationResult<Customer>.Failure(new OperationIssue("CUSTOMER.UNAVAILABLE", "The customer source is unavailable.", IssueCategory.Technical, correlation.Id)),
            _ => store.Read(customerId, clock.GetUtcNow(), correlation.Id)
        };
        return Task.FromResult(result);
    }, cancellationToken);
}
public sealed class FakeBillingSourceAdapter(ICorrelationContext correlation, TimeProvider clock, SourceExecutor executor) : IBillingSourceAdapter
{
    public Task<OperationResult<BillingSummary>> GetSummaryAsync(string customerId, CancellationToken cancellationToken)
        => executor.ExecuteAsync(new("BILLING", "GetSummary", IsReadOnly: true), token =>
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(customerId == "CUST-WARN"
            ? OperationResult<BillingSummary>.Failure(new OperationIssue("BILLING.TIMEOUT", "Billing information is temporarily unavailable.", IssueCategory.Technical, correlation.Id))
            : OperationResult<BillingSummary>.Success(new(125.50m, "USD", clock.GetUtcNow())));
    }, cancellationToken);
}
