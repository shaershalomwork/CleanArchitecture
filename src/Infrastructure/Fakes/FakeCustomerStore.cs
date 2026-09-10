using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Sources.CustomerRegistry;

namespace CleanArchitecture.Infrastructure.Fakes;

// Per-host state. Legacy synthetic reads materialize ordinary IDs; tombstones prevent
// a subsequent lookup from synthesizing a record that was deliberately deleted.
public sealed class FakeCustomerStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _records = new(StringComparer.Ordinal)
    {
        ["CUST-001"] = "Example Customer", ["CUST-WARN"] = "Example Customer"
    };
    private readonly HashSet<string> _deleted = new(StringComparer.Ordinal);

    public OperationResult<Customer> Read(string id, DateTimeOffset observedAt, string correlationId)
    {
        lock (_gate)
        {
            if (_deleted.Contains(id)) return CustomerWriteResults.NotFound<Customer>(correlationId);
            if (!_records.TryGetValue(id, out var name)) _records[id] = name = "Example Customer";
            return OperationResult<Customer>.Success(new(id, name, observedAt));
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
            _deleted.Remove(id);
            return OperationResult<Customer>.Success(new(id, name, observedAt));
        }
    }

    public OperationResult<NoData> Delete(string id, string correlationId)
    {
        lock (_gate)
        {
            if (!_records.Remove(id)) return CustomerWriteResults.NotFound<NoData>(correlationId);
            _deleted.Add(id);
            return OperationResult<NoData>.Success(new());
        }
    }
}
