using CleanArchitecture.Application.Customers.Sources;

namespace CleanArchitecture.Application.Customers.Commands;

// DisplayName is the only editable field. Missing and explicit null both fail validation;
// future optional fields must retain presence separately from their value.
public sealed record PatchCustomerCommand(string CustomerId, string? DisplayName) : IRequest<OperationResult<Customer>>;

public sealed class PatchCustomerCommandValidator : AbstractValidator<PatchCustomerCommand>
{
    public PatchCustomerCommandValidator()
    {
        RuleFor(x => x.CustomerId).CustomerId();
        RuleFor(x => x.DisplayName).DisplayName();
    }
}

public sealed class PatchCustomerCommandHandler(ICustomerWriteSourceAdapter customers)
    : IRequestHandler<PatchCustomerCommand, OperationResult<Customer>>
{
    public Task<OperationResult<Customer>> Handle(PatchCustomerCommand request, CancellationToken cancellationToken) =>
        customers.PatchAsync(request.CustomerId, request.DisplayName!, cancellationToken);
}
