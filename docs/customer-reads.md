# Customer read examples

The configured CustomerRegistry is the authority for customer existence. Existing live registry rows count as registered customers, including records created by other systems. No registration flag or application-owned database is introduced.

| Method and route | Success data | Permission |
| --- | --- | --- |
| GET `/api/customers` | Array of customerId, displayName, observedAt | customers.read |
| GET `/api/customers/{customerId}/overview` | Registered profile and optional billing summary | customers.read |

Both endpoints use the existing `CustomerOverview.Read` policy. Write permission alone grants neither read. Web authorizes and binds requests, sends a top-level MediatR query, and maps `OperationResult<T>` to `ApiOperationResponse<T>`. Application owns validation, sorting, orchestration, and explicit billing degradation. Infrastructure implements `ICustomerSourceAdapter` and owns SQL, connection lifetimes, source validation, and read resilience. Commands retain the separate `ICustomerWriteSourceAdapter`.

## Registration and overview

Development fake state starts empty and resets when its host restarts. Only explicit creation adds a customer. Looking up an arbitrary ID never changes the registry, and deleted customers stay absent until explicitly recreated. Tests register their own fixtures through the write capability or API; application startup does not seed them.

Overview validates the ID using the same customer rules as commands, then awaits the registry lookup. Missing customers return 404 with `CUSTOMER.NOT_FOUND`, and required-source failures return their mapped error. Neither path requests billing. After a successful lookup, optional live or fake billing can enrich that registered customer without changing their ID or stored display name.

Only `BILLING.TIMEOUT`, `BILLING.UNAVAILABLE`, and `BILLING.CIRCUIT_OPEN` permit HTTP 200 / Warning. Unavailable billing fields and payment attention remain null. Other billing failures terminate the overview. Source observations do not constitute an atomic cross-system snapshot; `observedAt` records the read time, not a registration timestamp.

Caller cancellation propagates, including cancellation from the existing 15-second HTTP request budget. Each sequential source call retains its own execution budget; the overall request deadline can expire before both source budgets are consumed. Request cancellation is not a degradable billing failure.

## Listing contract

`GetCustomersQuery` calls `GetCustomersAsync(CancellationToken)` and orders the complete result by customer ID using ordinal comparison. The endpoint returns the existing envelope with an array in `data`. An empty registry is HTTP 200 / Success with `data: []`; source failures are errors with `data: null`. Listing never contacts billing, includes no synthesized rows, and has no pagination or hidden truncation. Teams adopting this full-list example for large registries should introduce an explicit paginated contract.

All providers execute as `SourceOperation("CUSTOMER", "GetCustomers", IsReadOnly: true)`. The existing executor owns retry, timeout, circuit, correlation, and telemetry behavior. Source rows must contain valid IDs and display names without duplicate IDs; an invalid row rejects the entire response.

SQL Server uses parameterless `dbo.GetCustomers`, returning `Id` and `DisplayName` rows and return code 0, including when empty. SQLite and Oracle select those columns from the existing `Customers` table. SQLite connections remain read-only. The SQL Server example procedure is in the disposable fixture SQL. A source owner must deploy the procedure and grant execute permission before deploying this application change. The application never runs fixture SQL or migrates an external schema.

## Try the workflow

1. Sign in to Angular with the Reader/writer development profile and create `CUST-001` with your own display name. For API-only use, create a local writer token with `dotnet user-jwts` and POST the same record through Scalar or `Web.http`.
2. Open `/customers`, or GET `/api/customers` using a reader identity. Select the customer to open its overview.
3. Replace or patch its display name, then return to the list. The API supplies the updated name.
4. Delete the customer and return to the list. It disappears, and its overview returns 404.
5. Explicitly create `CUST-WARN` to demonstrate optional fake billing failure. `CUST-FAIL` and `CUST-MISSING` remain reserved fake error scenarios and never produce list records.

The UI loads the list on entry and refresh, shows loading/empty/error states separately, and retains exact-ID lookup. Overview URLs remain `/customers/:customerId/overview`. Creation and management links follow write capabilities; writer-only workflows never request a list or overview. Customer data is not persisted in browser storage.
