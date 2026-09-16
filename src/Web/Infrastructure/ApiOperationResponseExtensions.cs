using CleanArchitecture.Web.Contracts;

namespace CleanArchitecture.Web.Infrastructure;

public static class ApiOperationResponseExtensions
{
    /// <summary>
    /// Documents shared envelope responses for authenticated source-backed operations.
    /// Success and warning are 200, matching OperationResultMapper. Add business-specific
    /// responses (such as 404, 409 and 422) beside the endpoint that can return them.
    /// </summary>
    public static RouteHandlerBuilder ProducesApiOperationResponses<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.Produces<ApiOperationResponse<T>>(StatusCodes.Status200OK)
            .Produces<ApiOperationResponse<T>>(StatusCodes.Status400BadRequest)
            .Produces<ApiOperationResponse<T>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiOperationResponse<T>>(StatusCodes.Status403Forbidden)
            .Produces<ApiOperationResponse<T>>(StatusCodes.Status500InternalServerError)
            .Produces<ApiOperationResponse<T>>(StatusCodes.Status502BadGateway)
            .Produces<ApiOperationResponse<T>>(StatusCodes.Status503ServiceUnavailable)
            .Produces<ApiOperationResponse<T>>(StatusCodes.Status504GatewayTimeout);
}
