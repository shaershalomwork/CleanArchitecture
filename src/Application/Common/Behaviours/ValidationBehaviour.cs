using CleanArchitecture.Application.Common.Interfaces;

namespace CleanArchitecture.Application.Common.Behaviours;
public sealed class ValidationBehaviour<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators, ICorrelationContext correlation)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IOperationResult<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var issues = new List<OperationIssue>();
        foreach (var validator in validators)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            issues.AddRange(validation.Errors.Select(e => new OperationIssue(
                e.ErrorCode, e.ErrorMessage, IssueCategory.Validation, correlation.Id, e.PropertyName)));
        }
        return issues.Count == 0 ? await next(cancellationToken)
            : TResponse.Failure(issues.OrderBy(i => i.Target).ThenBy(i => i.Code));
    }
}
