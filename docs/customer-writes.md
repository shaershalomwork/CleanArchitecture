# Customer write examples

The write endpoints follow Web → MediatR command → Application-owned ICustomerWriteSourceAdapter → Infrastructure. Validation runs before source access. SQL Server, SQLite, Oracle, and deterministic fakes implement the same capability. Billing records are never modified.

| Method and route | Body | Success data | Business errors |
| --- | --- | --- | --- |
| POST `/api/customers` | `{"customerId":"WRITE-1","displayName":"Example"}` | customerId, displayName, observedAt | 409 duplicate ID |
| PUT `/api/customers/WRITE-1` | `{"displayName":"Replacement"}` | customerId, displayName, observedAt | 404 absent customer |
| PATCH `/api/customers/WRITE-1` | `{"displayName":"Partial change"}` | customerId, displayName, observedAt | 404 absent customer |
| DELETE `/api/customers/WRITE-1` | none | customerId | 404 absent/already deleted |

All successful operations return HTTP 200 and `ApiOperationResponse<T>` with status, data, issues, and correlationId. Deletion returns a receipt, not a 204. Errors have null data. The shared response extension documents 200/400/401/403/500/502/503/504; business-specific responses remain beside each endpoint. OperationResultMapper remains the authority for HTTP translation, including 409 conflict and 502 unknown write outcome.

Customer IDs are immutable, required, at most 50 characters, and use letters, digits, or hyphens. DisplayName must be nonblank and at most 200 characters. PUT replaces all editable fields and does not upsert. PATCH accepts a partial JSON object, not an RFC 6902 operation array. DisplayName is currently the only editable field, so a valid PATCH must supply it; `{}`, null values, identity fields, unknown fields, and malformed bodies return 400. Future nullable patch fields must distinguish omission from explicit null. Updates use last-write-wins semantics without an ETag or version token.

## Authorization and local testing

`Customer.Write` requires `permissions=customers.write`. The existing overview endpoint still requires only `customers.read`; write-only identities do not gain read access. The default Development reader remains read-only. Select the writer or reader-writer demo profile for browser writes, or create a local token with dotnet user-jwts create --project src/Web --claim permissions=customers.write. External deployments continue to use externally issued tokens. See [workspace and authentication](workspace.md) for the complete workflow. For OIDC cookie sessions, obtain `/auth/antiforgery`, retain the cookie, and send the returned token in `X-CSRF-TOKEN` with each mutation. Antiforgery does not replace authorization.

`src/Web/Web.http` includes requests with a token placeholder. Functional tests exercise both fake state and a real disposable SQLite file, with writer/read-only identities and antiforgery tokens. JWT tests verify bearer writes independently of cookies.

Fake state starts empty, belongs to one application host, and resets on restart. Explicit POST creates records; lookups and listing never create them. Deleted IDs return 404 until explicitly recreated. Create `CUST-001` or `CUST-WARN` before trying complete or partial billing examples. `CUST-MISSING` and `CUST-FAIL` remain reserved error scenarios. Failure simulations do not contact a database. See [customer reads](customer-reads.md) for the registration and list contracts.

## Source contracts and failure handling

SQL Server uses `dbo.CreateCustomer(@CustomerId nvarchar(50), @DisplayName nvarchar(200))`, `dbo.UpdateCustomer` with the same inputs, and `dbo.DeleteCustomer(@CustomerId nvarchar(50))`. These procedures return 0 on success and update/delete return 404 when absent. Duplicate-key exceptions from creation map to 409. The adapter owns the local transaction; procedures must not commit independently. Example procedures are in the disposable SQL fixture, which application startup never executes.

SQLite uses a separate ReadWrite connection factory that refuses to create a missing file. Lookup connections remain ReadOnly. Oracle uses its source-owned table contract; see [Oracle setup](oracle.md). Multi-row mutations against an invalid schema roll back rather than returning success.

Writes have no automatic retries. A timeout or lost connection can occur after a commit, so `CUSTOMER.OUTCOME_UNKNOWN` means the caller must reconcile against the system of record before another attempt. This API does not provide idempotency keys or distributed transactions. Source observations are not a cross-system snapshot.
