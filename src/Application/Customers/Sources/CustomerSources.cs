namespace CleanArchitecture.Application.Customers.Sources;

public sealed record Customer(string Id, string DisplayName, DateTimeOffset ObservedAt);
public sealed record BillingSummary(decimal OutstandingBalance, string Currency, DateTimeOffset ObservedAt);
public interface ICustomerSourceAdapter
{
    Task<OperationResult<Customer>> GetCustomerAsync(string customerId, CancellationToken cancellationToken);
    Task<OperationResult<IReadOnlyList<Customer>>> GetCustomersAsync(CancellationToken cancellationToken);
}
public interface ICustomerWriteSourceAdapter
{
    Task<OperationResult<Customer>> CreateAsync(string customerId, string displayName, CancellationToken cancellationToken);
    Task<OperationResult<Customer>> ReplaceAsync(string customerId, string displayName, CancellationToken cancellationToken);
    Task<OperationResult<Customer>> PatchAsync(string customerId, string displayName, CancellationToken cancellationToken);
    Task<OperationResult<NoData>> DeleteAsync(string customerId, CancellationToken cancellationToken);
}
public interface IBillingSourceAdapter
{
    Task<OperationResult<BillingSummary>> GetSummaryAsync(string customerId, CancellationToken cancellationToken);
}
