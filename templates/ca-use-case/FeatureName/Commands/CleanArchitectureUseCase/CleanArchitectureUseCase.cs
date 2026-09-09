using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Results;

namespace CleanArchitecture.Application.FeatureName.Commands.CleanArchitectureUseCase;

public sealed record CleanArchitectureUseCaseCommand : IRequest<OperationResult<TReturnType>>;
public sealed class CleanArchitectureUseCaseCommandValidator : AbstractValidator<CleanArchitectureUseCaseCommand>
{
    public CleanArchitectureUseCaseCommandValidator() { }
}
public sealed class CleanArchitectureUseCaseCommandHandler(ICorrelationContext correlation)
    : IRequestHandler<CleanArchitectureUseCaseCommand, OperationResult<TReturnType>>
{
    public Task<OperationResult<TReturnType>> Handle(CleanArchitectureUseCaseCommand request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // TODO: Inject narrow source capabilities and explicitly decide whether degraded data is usable.
        return Task.FromResult(OperationResult<TReturnType>.Failure(new OperationIssue(
            "USE_CASE.NOT_IMPLEMENTED", "This use case has not been implemented.", IssueCategory.Technical, correlation.Id)));
    }
}
