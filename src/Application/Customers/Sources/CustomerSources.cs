namespace CleanArchitecture.Application.Customers.Sources;

public sealed record Customer(string Id, string DisplayName, DateTimeOffset ObservedAt);
public sealed record BillingSummary(decimal OutstandingBalance, string Currency, DateTimeOffset ObservedAt);
public interface ICustomerSourceAdapter
{
    Task<OperationResult<Customer>> GetCustomerAsync(string customerId, CancellationToken cancellationToken);
}
public interface IBillingSourceAdapter
{
    Task<OperationResult<BillingSummary>> GetSummaryAsync(string customerId, CancellationToken cancellationToken);
}
