using CleanArchitecture.Application.Customers.Sources;

namespace CleanArchitecture.Application.Customers.Commands;

public sealed record CreateCustomerCommand(string CustomerId, string? DisplayName) : IRequest<OperationResult<Customer>>;

public sealed class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator()
    {
        RuleFor(x => x.CustomerId).CustomerId();
        RuleFor(x => x.DisplayName).DisplayName();
    }
}

public sealed class CreateCustomerCommandHandler(ICustomerWriteSourceAdapter customers)
    : IRequestHandler<CreateCustomerCommand, OperationResult<Customer>>
{
    public Task<OperationResult<Customer>> Handle(CreateCustomerCommand request, CancellationToken cancellationToken) =>
        customers.CreateAsync(request.CustomerId, request.DisplayName!, cancellationToken);
}
