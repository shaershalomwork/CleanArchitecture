using CleanArchitecture.Application.Common.Results;
using CleanArchitecture.Web.Contracts;
namespace CleanArchitecture.Web.Infrastructure;

public static class OperationResultMapper
{
    public static IResult ToHttp<T, TOutput>(this OperationResult<T> result, Func<T, TOutput> map, string correlationId)
        where TOutput : class =>
        Results.Json(new ApiOperationResponse<TOutput>(result.Status.ToString(),
            result.HasData ? map(result.Data) : null, result.Issues.Select(i => i.ToResponse()).ToArray(), correlationId),
            statusCode: result.Status == OperationStatus.Error ? StatusCode(result.Issues[0]) : 200);

    public static int StatusCode(OperationIssue issue)
    {
        if (issue.Category == IssueCategory.Validation) return 400;
        return issue.Code switch
        {
            "AUTH.UNAUTHENTICATED" => 401,
            "AUTH.FORBIDDEN" => 403,
            "CUSTOMER.NOT_FOUND" => 404,
            "BILLING.REJECTED" => 422,
            "APPLICATION.UNEXPECTED" or "USE_CASE.NOT_IMPLEMENTED" => 500,
            _ when issue.Code.EndsWith(".CONFLICT", StringComparison.Ordinal) => 409,
            _ when issue.Code.EndsWith(".TIMEOUT", StringComparison.Ordinal) => 504,
            _ when issue.Code.EndsWith(".UNAVAILABLE", StringComparison.Ordinal) || issue.Code.EndsWith(".CIRCUIT_OPEN", StringComparison.Ordinal) => 503,
            _ => 502
        };
    }

    public static Task WriteErrorAsync(HttpContext context, int status, string code, string message, CancellationToken token = default)
    {
        var correlation = context.TraceIdentifier;
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ApiOperationResponse<object>("Error", null,
            [new(code, message, status is 401 or 403 ? "Authorization" : status == 400 ? "Validation" : "Technical", correlation, null)], correlation), token);
    }
}
