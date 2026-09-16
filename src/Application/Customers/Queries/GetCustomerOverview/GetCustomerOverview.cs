using CleanArchitecture.Application.Customers.Sources;

namespace CleanArchitecture.Application.Customers.Queries.GetCustomerOverview;

public sealed record GetCustomerOverviewQuery(string CustomerId) : IRequest<OperationResult<CustomerOverview>>;
public sealed record CustomerOverview(Customer Customer, BillingSummary? Billing, bool BillingAvailable, bool? HasOutstandingBalance);

public sealed class GetCustomerOverviewQueryValidator : AbstractValidator<GetCustomerOverviewQuery>
{
    public GetCustomerOverviewQueryValidator()
    {
        RuleFor(x => x.CustomerId).CustomerId();
    }
}

public sealed class GetCustomerOverviewQueryHandler(ICustomerSourceAdapter customers, IBillingSourceAdapter billing)
    : IRequestHandler<GetCustomerOverviewQuery, OperationResult<CustomerOverview>>
{
    private static readonly HashSet<string> DegradableBillingCodes =
        ["BILLING.TIMEOUT", "BILLING.UNAVAILABLE", "BILLING.CIRCUIT_OPEN"];

    public async Task<OperationResult<CustomerOverview>> Handle(GetCustomerOverviewQuery request, CancellationToken cancellationToken)
    {
        var customer = await customers.GetCustomerAsync(request.CustomerId, cancellationToken);
        if (!customer.HasData)
            return OperationResult<CustomerOverview>.Failure(customer.Issues);
        var summary = await billing.GetSummaryAsync(customer.Data.Id, cancellationToken);
        if (!summary.HasData)
        {
            if (summary.Issues.All(i => DegradableBillingCodes.Contains(i.Code)))
                return OperationResult<CustomerOverview>.Warning(
                    new(customer.Data, null, false, null), customer.Issues.Concat(summary.Issues));
            return OperationResult<CustomerOverview>.Failure(summary.Issues);
        }
        var overview = new CustomerOverview(customer.Data, summary.Data, true, summary.Data.OutstandingBalance > 0);
        var issues = customer.Issues.Concat(summary.Issues).ToArray();
        return issues.Length == 0 ? OperationResult<CustomerOverview>.Success(overview)
            : OperationResult<CustomerOverview>.Warning(overview, issues);
    }
}
