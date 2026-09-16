using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Execution;

namespace CleanArchitecture.Infrastructure.Sources.CustomerRegistry;

internal static class CustomerReadResults
{
    public static OperationResult<IReadOnlyList<Customer>> FromRows(
        IEnumerable<(string Id, string DisplayName)> rows, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var customers = new List<Customer>();
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(row.Id) || row.Id.Length > 50 ||
                row.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
                string.IsNullOrWhiteSpace(row.DisplayName) || row.DisplayName.Length > 200 || !ids.Add(row.Id))
                throw new SourceFailureException("INVALID_RESPONSE");
            customers.Add(new(row.Id, row.DisplayName, observedAt));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return OperationResult<IReadOnlyList<Customer>>.Success(customers.ToArray());
    }
}
