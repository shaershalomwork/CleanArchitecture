using CleanArchitecture.Application.Customers.Sources;

namespace CleanArchitecture.Application.Customers.Commands;

public sealed record DeleteCustomerCommand(string CustomerId) : IRequest<OperationResult<NoData>>;

public sealed class DeleteCustomerCommandValidator : AbstractValidator<DeleteCustomerCommand>
{
    public DeleteCustomerCommandValidator() => RuleFor(x => x.CustomerId).CustomerId();
}

public sealed class DeleteCustomerCommandHandler(ICustomerWriteSourceAdapter customers)
    : IRequestHandler<DeleteCustomerCommand, OperationResult<NoData>>
{
    public Task<OperationResult<NoData>> Handle(DeleteCustomerCommand request, CancellationToken cancellationToken) =>
        customers.DeleteAsync(request.CustomerId, cancellationToken);
}
