using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Sources.CustomerRegistry;

namespace CleanArchitecture.Infrastructure.Fakes;

// Per-host registry. Only explicit writes add customers; reads never change state.
public sealed class FakeCustomerStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _records = new(StringComparer.Ordinal);

    public OperationResult<Customer> Read(string id, DateTimeOffset observedAt, string correlationId)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(id, out var name)) return CustomerWriteResults.NotFound<Customer>(correlationId);
            return OperationResult<Customer>.Success(new(id, name, observedAt));
        }
    }

    public OperationResult<IReadOnlyList<Customer>> ReadAll(DateTimeOffset observedAt)
    {
        lock (_gate)
        {
            return OperationResult<IReadOnlyList<Customer>>.Success(
                _records.Select(row => new Customer(row.Key, row.Value, observedAt)).ToArray());
        }
    }

    public OperationResult<Customer> Save(bool create, string id, string name, DateTimeOffset observedAt, string correlationId)
    {
        lock (_gate)
        {
            if (create && (_records.ContainsKey(id) || id is "CUST-MISSING" or "CUST-FAIL"))
                return CustomerWriteResults.Conflict<Customer>(correlationId);
            if (!create && !_records.ContainsKey(id)) return CustomerWriteResults.NotFound<Customer>(correlationId);
            _records[id] = name;
            return OperationResult<Customer>.Success(new(id, name, observedAt));
        }
    }

    public OperationResult<NoData> Delete(string id, string correlationId)
    {
        lock (_gate)
        {
            if (!_records.Remove(id)) return CustomerWriteResults.NotFound<NoData>(correlationId);
            return OperationResult<NoData>.Success(new());
        }
    }
}
