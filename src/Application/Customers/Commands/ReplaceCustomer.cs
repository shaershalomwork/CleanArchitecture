using CleanArchitecture.Application.Customers.Sources;

namespace CleanArchitecture.Application.Customers.Commands;

public sealed record ReplaceCustomerCommand(string CustomerId, string? DisplayName) : IRequest<OperationResult<Customer>>;

public sealed class ReplaceCustomerCommandValidator : AbstractValidator<ReplaceCustomerCommand>
{
    public ReplaceCustomerCommandValidator()
    {
        RuleFor(x => x.CustomerId).CustomerId();
        RuleFor(x => x.DisplayName).DisplayName();
    }
}

public sealed class ReplaceCustomerCommandHandler(ICustomerWriteSourceAdapter customers)
    : IRequestHandler<ReplaceCustomerCommand, OperationResult<Customer>>
{
    public Task<OperationResult<Customer>> Handle(ReplaceCustomerCommand request, CancellationToken cancellationToken) =>
        customers.ReplaceAsync(request.CustomerId, request.DisplayName!, cancellationToken);
}
