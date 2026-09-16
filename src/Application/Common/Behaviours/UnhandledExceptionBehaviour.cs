using CleanArchitecture.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Application.Common.Behaviours;
public sealed class UnhandledExceptionBehaviour<TRequest, TResponse>(
    ILogger<UnhandledExceptionBehaviour<TRequest, TResponse>> logger, ICorrelationContext correlation)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IOperationResult<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try { return await next(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError("Unexpected failure in {UseCase}; code {Code}; exception {ExceptionType}; correlation {CorrelationId}",
                typeof(TRequest).Name, "APPLICATION.UNEXPECTED", exception.GetType().Name, correlation.Id);
            return TResponse.Failure([new("APPLICATION.UNEXPECTED", "The operation could not be completed.", IssueCategory.Technical, correlation.Id)]);
        }
    }
}
