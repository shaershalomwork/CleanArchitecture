using CleanArchitecture.Application.Common.Results;
using CleanArchitecture.Application.Customers.Queries.GetCustomerOverview;
using CleanArchitecture.Application.Customers.Queries.GetCustomers;
using CleanArchitecture.Application.Customers.Sources;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Customers;

public class CustomerReadTests
{
    [TestCase("CUSTOMER.NOT_FOUND")]
    [TestCase("CUSTOMER.UNAVAILABLE")]
    public async Task RegistryFailurePreventsBillingAccess(string code)
    {
        var issue = new OperationIssue(code, "Unavailable", IssueCategory.Business, "trace");
        var source = new Source { CustomerResult = OperationResult<Customer>.Failure(issue) };
        var result = await new GetCustomerOverviewQueryHandler(source, new UnexpectedBilling()).Handle(new("UNKNOWN"), default);
        result.HasData.ShouldBeFalse();
        result.Issues.ShouldBe(new[] { issue });
    }

    [Test] public async Task ListsAllCustomersInOrdinalOrderAndForwardsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var rows = new[] { "z", "B", "a", "A" }.Select(id => new Customer(id, id, DateTimeOffset.UtcNow)).ToArray();
        var source = new Source { ListResult = OperationResult<IReadOnlyList<Customer>>.Success(rows) };
        var result = await new GetCustomersQueryHandler(source).Handle(new(), cancellation.Token);
        result.Data.Select(x => x.Id).ShouldBe(new[] { "A", "B", "a", "z" });
        source.Token.ShouldBe(cancellation.Token);
    }

    [Test] public async Task ListFailuresAreNotEmptySuccesses()
    {
        var issue = new OperationIssue("CUSTOMER.TIMEOUT", "Unavailable", IssueCategory.Technical, "trace");
        var source = new Source { ListResult = OperationResult<IReadOnlyList<Customer>>.Failure(issue) };
        var result = await new GetCustomersQueryHandler(source).Handle(new(), default);
        result.HasData.ShouldBeFalse();
        result.Issues.ShouldBe(new[] { issue });
    }

    [Test] public void ListCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Should.ThrowAsync<OperationCanceledException>(() => new GetCustomersQueryHandler(new Source()).Handle(new(), cancellation.Token));
    }

    private sealed class Source : ICustomerSourceAdapter
    {
        public OperationResult<Customer> CustomerResult { get; init; } = OperationResult<Customer>.Success(new("ID", "Name", DateTimeOffset.UtcNow));
        public OperationResult<IReadOnlyList<Customer>> ListResult { get; init; } = OperationResult<IReadOnlyList<Customer>>.Success([]);
        public CancellationToken Token { get; private set; }
        public Task<OperationResult<Customer>> GetCustomerAsync(string customerId, CancellationToken cancellationToken) => Task.FromResult(CustomerResult);
        public Task<OperationResult<IReadOnlyList<Customer>>> GetCustomersAsync(CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            return Task.FromResult(ListResult);
        }
    }

    private sealed class UnexpectedBilling : IBillingSourceAdapter
    {
        public Task<OperationResult<BillingSummary>> GetSummaryAsync(string customerId, CancellationToken cancellationToken) =>
            throw new AssertionException("Billing must not be requested without a registered customer.");
    }
}
