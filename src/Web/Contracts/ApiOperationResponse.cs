using CleanArchitecture.Application.Common.Results;
namespace CleanArchitecture.Web.Contracts;
public sealed record ApiIssue(string Code, string Message, string Category, string CorrelationId, string? Target);
public sealed record ApiOperationResponse<T>(string Status, T? Data, IReadOnlyList<ApiIssue> Issues, string CorrelationId) where T : class;
public sealed record CustomerOverviewResponse(string CustomerId, string DisplayName, bool BillingAvailable,
    decimal? OutstandingBalance, string? Currency, bool? HasOutstandingBalance,
    DateTimeOffset CustomerObservedAt, DateTimeOffset? BillingObservedAt);

public static class ApiIssueMapping
{
    public static ApiIssue ToResponse(this OperationIssue issue) =>
        new(issue.Code, issue.Message, issue.Category.ToString(), issue.CorrelationId, issue.Target);
}
