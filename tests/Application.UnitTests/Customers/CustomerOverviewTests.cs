using CleanArchitecture.Application.Common.Results;
using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Application.Customers.Queries.GetCustomerOverview;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Customers;
public class CustomerOverviewTests
{
    [TestCase("BILLING.TIMEOUT", true)]
    [TestCase("BILLING.UNAVAILABLE", true)]
    [TestCase("BILLING.CIRCUIT_OPEN", true)]
    [TestCase("BILLING.INVALID_RESPONSE", false)]
    [TestCase("BILLING.AUTHENTICATION_FAILED", false)]
    [TestCase("BILLING.NOT_FOUND", false)]
    public async Task DegradationIsExplicit(string code, bool permitsPartial)
    {
        var handler = new GetCustomerOverviewQueryHandler(new Customers(), new Billing(code));
        var result = await handler.Handle(new("CUST-001"), default);
        result.Status.ShouldBe(permitsPartial ? OperationStatus.Warning : OperationStatus.Error);
        if (permitsPartial)
        {
            result.Data.Billing.ShouldBeNull();
            result.Data.BillingAvailable.ShouldBeFalse();
            result.Data.HasOutstandingBalance.ShouldBeNull();
        }
        result.Issues[0].Code.ShouldBe(code);
    }
    [Test] public async Task ZeroBalanceIsAvailableData()
    {
        var result = await new GetCustomerOverviewQueryHandler(new Customers(), new Billing(null)).Handle(new("CUST-001"), default);
        result.Status.ShouldBe(OperationStatus.Success);
        result.Data.BillingAvailable.ShouldBeTrue();
        result.Data.HasOutstandingBalance.ShouldBe(false);
    }
    [Test] public async Task RequiredFailureIsPrimaryWhenBothFail()
    {
        var result = await new GetCustomerOverviewQueryHandler(new Customers(true), new Billing("BILLING.TIMEOUT")).Handle(new("CUST-001"), default);
        result.Status.ShouldBe(OperationStatus.Error);
        result.Issues[0].Code.ShouldBe("CUSTOMER.UNAVAILABLE");
    }
    private sealed class Customers(bool fail = false) : ICustomerSourceAdapter
    {
        public Task<OperationResult<IReadOnlyList<Customer>>> GetCustomersAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<OperationResult<Customer>> GetCustomerAsync(string id, CancellationToken ct) =>
            Task.FromResult(fail ? OperationResult<Customer>.Failure(new OperationIssue("CUSTOMER.UNAVAILABLE", "Unavailable", IssueCategory.Technical, "trace"))
            : OperationResult<Customer>.Success(new(id, "Name", DateTimeOffset.UtcNow)));
    }
    private sealed class Billing(string? code) : IBillingSourceAdapter
    {
        public Task<OperationResult<BillingSummary>> GetSummaryAsync(string id, CancellationToken ct) =>
            Task.FromResult(code is not null ? OperationResult<BillingSummary>.Failure(new OperationIssue(code, "Unavailable", IssueCategory.Technical, "trace"))
            : OperationResult<BillingSummary>.Success(new(0, "USD", DateTimeOffset.UtcNow)));
    }
}
