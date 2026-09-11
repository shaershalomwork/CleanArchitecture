using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Customers.Commands;
using CleanArchitecture.Application.Customers.Queries.GetCustomerOverview;
using CleanArchitecture.Application.Customers.Queries.GetCustomers;
using CleanArchitecture.Web.Contracts;
namespace CleanArchitecture.Web.Endpoints;

public sealed class Customers : IEndpointGroup
{
    public static string RoutePrefix => "/api/customers";
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetCustomers).RequireAuthorization("CustomerOverview.Read")
            .ProducesApiOperationResponses<IReadOnlyList<CustomerResponse>>();
        group.MapGet(GetCustomerOverview, "{customerId}/overview")
            .RequireAuthorization("CustomerOverview.Read")
            .ProducesApiOperationResponses<CustomerOverviewResponse>()
            .Produces<ApiOperationResponse<CustomerOverviewResponse>>(404)
            .Produces<ApiOperationResponse<CustomerOverviewResponse>>(422);
        group.MapPost(CreateCustomer).RequireAuthorization("Customer.Write")
            .ProducesApiOperationResponses<CustomerResponse>()
            .Produces<ApiOperationResponse<CustomerResponse>>(409);
        group.MapPut(ReplaceCustomer, "{customerId}").RequireAuthorization("Customer.Write")
            .ProducesApiOperationResponses<CustomerResponse>()
            .Produces<ApiOperationResponse<CustomerResponse>>(404);
        group.MapPatch(PatchCustomer, "{customerId}").RequireAuthorization("Customer.Write")
            .ProducesApiOperationResponses<CustomerResponse>()
            .Produces<ApiOperationResponse<CustomerResponse>>(404);
        group.MapDelete(DeleteCustomer, "{customerId}").RequireAuthorization("Customer.Write")
            .ProducesApiOperationResponses<CustomerDeletionResponse>()
            .Produces<ApiOperationResponse<CustomerDeletionResponse>>(404);
    }
    [EndpointSummary("Get all registered customers")]
    [EndpointDescription("Requires customers.read. Returns every registered customer's ID, display name, and observation time, ordered by customer ID. The success envelope contains an empty array when the registry is empty. This read does not request billing and is not paginated.")]
    public static async Task<IResult> GetCustomers(ISender sender, ICorrelationContext correlation, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCustomersQuery(), cancellationToken);
        return result.ToHttp(rows => (IReadOnlyList<CustomerResponse>)rows.Select(x => new CustomerResponse(x.Id, x.DisplayName, x.ObservedAt)).ToArray(), correlation.Id);
    }

    [EndpointSummary("Get a customer's profile and billing overview")]
    [EndpointDescription("Requires customers.read. Combines the required customer record with optional billing data. HTTP 200 can contain Success or Warning; a billing outage leaves balance fields null. Returns 404 when the customer is absent and 422 when billing rejects the request.")]
    public static async Task<IResult> GetCustomerOverview(ISender sender, ICorrelationContext correlation,
        string customerId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCustomerOverviewQuery(customerId), cancellationToken);
        return result.ToHttp(x => new CustomerOverviewResponse(x.Customer.Id, x.Customer.DisplayName, x.BillingAvailable,
            x.Billing?.OutstandingBalance, x.Billing?.Currency, x.HasOutstandingBalance,
            x.Customer.ObservedAt, x.Billing?.ObservedAt), correlation.Id);
    }
    [EndpointSummary("Create a customer")]
    [EndpointDescription("Requires customers.write. Creates a customer with a caller-supplied immutable ID and display name. Returns a 200 success envelope or 409 for an existing ID. Writes are never automatically retried; reconcile CUSTOMER.OUTCOME_UNKNOWN before retrying.")]
    public static async Task<IResult> CreateCustomer(ISender sender, ICorrelationContext correlation,
        CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateCustomerCommand(request.CustomerId, request.DisplayName), cancellationToken);
        return result.ToHttp(x => new CustomerResponse(x.Id, x.DisplayName, x.ObservedAt), correlation.Id);
    }

    [EndpointSummary("Replace a customer's editable details")]
    [EndpointDescription("Requires customers.write. Replaces all editable fields (currently displayName) of an existing customer without changing its ID. Missing fields fail validation; absent customers return 404. Returns a 200 envelope. Reconcile CUSTOMER.OUTCOME_UNKNOWN before retrying.")]
    public static async Task<IResult> ReplaceCustomer(ISender sender, ICorrelationContext correlation,
        string customerId, ReplaceCustomerRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ReplaceCustomerCommand(customerId, request.DisplayName), cancellationToken);
        return result.ToHttp(x => new CustomerResponse(x.Id, x.DisplayName, x.ObservedAt), correlation.Id);
    }

    [EndpointSummary("Partially update a customer's details")]
    [EndpointDescription("Requires customers.write. Accepts a partial JSON object. Only displayName is editable; supplied fields change and other fields remain unchanged. Empty objects, null values, and unknown or immutable fields return 400; absent customers return 404. Returns a 200 envelope. Reconcile CUSTOMER.OUTCOME_UNKNOWN before retrying.")]
    public static async Task<IResult> PatchCustomer(ISender sender, ICorrelationContext correlation,
        string customerId, PatchCustomerRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new PatchCustomerCommand(customerId, request.DisplayName), cancellationToken);
        return result.ToHttp(x => new CustomerResponse(x.Id, x.DisplayName, x.ObservedAt), correlation.Id);
    }

    [EndpointSummary("Delete a customer")]
    [EndpointDescription("Requires customers.write. Deletes an existing customer and returns a 200 envelope containing the customer ID. An absent or previously deleted customer returns 404. Billing is not modified. Reconcile CUSTOMER.OUTCOME_UNKNOWN before retrying.")]
    public static async Task<IResult> DeleteCustomer(ISender sender, ICorrelationContext correlation,
        string customerId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteCustomerCommand(customerId), cancellationToken);
        return result.ToHttp(_ => new CustomerDeletionResponse(customerId), correlation.Id);
    }
}
