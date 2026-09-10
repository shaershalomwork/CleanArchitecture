namespace CleanArchitecture.Infrastructure.Sources.CustomerRegistry;

internal static class CustomerWriteResults
{
    public static OperationResult<T> NotFound<T>(string correlationId) => OperationResult<T>.Failure(
        new OperationIssue("CUSTOMER.NOT_FOUND", "The requested customer was not found.", IssueCategory.Business, correlationId));

    public static OperationResult<T> Conflict<T>(string correlationId) => OperationResult<T>.Failure(
        new OperationIssue("CUSTOMER.CONFLICT", "A customer with this ID already exists.", IssueCategory.Business, correlationId));
}
