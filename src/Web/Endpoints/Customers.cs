using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Customers.Queries.GetCustomerOverview;
using CleanArchitecture.Web.Contracts;
namespace CleanArchitecture.Web.Endpoints;

public sealed class Customers : IEndpointGroup
{
    public static string RoutePrefix => "/api/customers";
    public static void Map(RouteGroupBuilder group)
    {
        group.RequireAuthorization("CustomerOverview.Read");
        group.MapGet(GetCustomerOverview, "{customerId}/overview")
            .Produces<ApiOperationResponse<CustomerOverviewResponse>>()
            .Produces<ApiOperationResponse<CustomerOverviewResponse>>(400)
            .Produces<ApiOperationResponse<CustomerOverviewResponse>>(401)
            .Produces<ApiOperationResponse<CustomerOverviewResponse>>(403)
            .Produces<ApiOperationResponse<CustomerOverviewResponse>>(404)
            .Produces<ApiOperationResponse<CustomerOverviewResponse>>(422)
            .Produces<ApiOperationResponse<CustomerOverviewResponse>>(500)
            .Produces<ApiOperationResponse<CustomerOverviewResponse>>(502)
            .Produces<ApiOperationResponse<CustomerOverviewResponse>>(503)
            .Produces<ApiOperationResponse<CustomerOverviewResponse>>(504);
    }
    public static async Task<IResult> GetCustomerOverview(ISender sender, ICorrelationContext correlation,
        string customerId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCustomerOverviewQuery(customerId), cancellationToken);
        return result.ToHttp(x => new CustomerOverviewResponse(x.Customer.Id, x.Customer.DisplayName, x.BillingAvailable,
            x.Billing?.OutstandingBalance, x.Billing?.Currency, x.HasOutstandingBalance,
            x.Customer.ObservedAt, x.Billing?.ObservedAt), correlation.Id);
    }
}
