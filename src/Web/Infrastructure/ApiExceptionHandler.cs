using Microsoft.AspNetCore.Diagnostics;
namespace CleanArchitecture.Web.Infrastructure;
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested) return false;
        if (exception is BadHttpRequestException)
            await OperationResultMapper.WriteErrorAsync(context, 400, "REQUEST.INVALID", "The request could not be read.", cancellationToken);
        else
        {
            logger.LogError(exception, "Unhandled HTTP failure; correlation {CorrelationId}", context.TraceIdentifier);
            await OperationResultMapper.WriteErrorAsync(context, 500, "APPLICATION.UNEXPECTED", "The operation could not be completed.", cancellationToken);
        }
        return true;
    }
}
