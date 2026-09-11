using CleanArchitecture.Application.Customers.Sources;

namespace CleanArchitecture.Application.Customers.Queries.GetCustomers;

public sealed record GetCustomersQuery : IRequest<OperationResult<IReadOnlyList<Customer>>>;

public sealed class GetCustomersQueryHandler(ICustomerSourceAdapter customers)
    : IRequestHandler<GetCustomersQuery, OperationResult<IReadOnlyList<Customer>>>
{
    public async Task<OperationResult<IReadOnlyList<Customer>>> Handle(GetCustomersQuery request, CancellationToken cancellationToken)
    {
        var result = await customers.GetCustomersAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Map<IReadOnlyList<Customer>>(rows => rows.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray());
    }
}
