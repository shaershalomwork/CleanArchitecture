# Oracle customer source

Oracle is an optional CustomerRegistry provider. SQL Server remains the default; Development/Test still use fakes until Live is explicitly selected.

```powershell
dotnet new di-sln -n MyIntegration -cf None --CustomerProvider Oracle
```

The Infrastructure project uses Dapper and Oracle.ManagedDataAccess.Core 23.26.300. This targets the existing .NET 10 application and requires a 64-bit runtime and Oracle Database 19c or newer. No Oracle EF Core integration or native Oracle client installation is required. See [Oracle system requirements](https://docs.oracle.com/en/database/oracle/oracle-database/26/odpnt/InstallSystemRequirements.html).

## Configure an existing application

Set `Sources:CustomerRegistry:Provider=Oracle`, `Mode=Live`, and supply the connection named by `ConnectionName` (default `CustomerRegistry`). Web includes a UserSecretsId; generated projects receive a distinct ID.

```powershell
# Run from the application root. Replace the placeholder locally; never commit credentials.
dotnet user-secrets set --project src/Web 'ConnectionStrings:CustomerRegistry' '<Oracle connection string>'
$env:Sources__CustomerRegistry__Mode = 'Live'
$env:Sources__CustomerRegistry__Provider = 'Oracle'
dotnet run --project src/Web --launch-profile https
```

A connection-string shape is `User Id=<user>;Password=<password>;Data Source=<host>:1521/<service>;Pooling=true`. Alternatively inject `ConnectionStrings__CustomerRegistry` through your deployment secret provider. The existing `AZURE_KEY_VAULT_ENDPOINT` configuration provider also applies. User secrets load in Development; deployments must supply their own secret provider. AppHost can inherit the same environment variable when it launches Web. Never place real connection strings in JSON, HTTP examples, fixture scripts, or logs.

Keep source credentials separate from caller identity. Read-only deployments need SELECT access; enabling the write API requires INSERT, UPDATE, and DELETE grants on the source table as well as the caller's `customers.write` permission. Startup validates configuration without contacting Oracle; the readiness check executes `SELECT 1 FROM DUAL` separately.

## Reference contract

The connected schema owns `Customers(Id NVARCHAR2(50) PRIMARY KEY, DisplayName NVARCHAR2(200) NOT NULL)`. Point reads use parameterized SQL with a two-row limit; missing customers map to `CUSTOMER.NOT_FOUND`. Listing selects all Id/DisplayName rows from the same table. Malformed or duplicate rows are invalid responses. Queries bind by name, including updates whose SQL parameter order differs from the supplied parameter object. See [customer reads](customer-reads.md).

POST inserts a new row; PUT/PATCH update DisplayName; DELETE removes one row. Each mutation uses an explicit local transaction and commits before returning success. Duplicate inserts map to `CUSTOMER.CONFLICT`. Zero-row updates/deletes map to not found. The application never creates or migrates the source schema. The SQL file in `tests/Infrastructure.IntegrationTests/Fixtures/customer-registry.oracle.sql` is only a disposable test contract.

Each execution attempt owns and disposes its connection. `ClientId` carries correlation; ODP.NET Core resets it on disposal before pooling. Oracle errors become safe source issues. Reads can retry transient failures through SourceExecutor; writes never automatically retry. A write timeout or disconnect reports `CUSTOMER.OUTCOME_UNKNOWN` (HTTP 502), requiring reconciliation with the source. Avoid provider replay, pipelining, or additional connection retry policies that bypass these execution guarantees.

## Verify

```powershell
pwsh build/verify.ps1
pwsh build/verify.ps1 -SourceIntegration
pwsh build/test.ps1 -CustomerProvider Oracle -BrowserTests
```

The offline suite needs no Oracle instance. SourceIntegration starts disposable SQL Server and Oracle Free containers through TestAppHost with generated passwords and no persistent volumes. The Oracle fixture pins the smaller official `container-registry.oracle.com/database/free:23.26.1.0-lite` image. Tests initialize only connections returned by their fixture host, never a configured corporate database. On a slow connection, prefetch that image with `docker pull` before running tests so its download does not consume the five-minute startup budget. The template checks cover both API-only and Angular with fake sources.

See [customer write examples](customer-writes.md) for request contracts, permissions, and reconciliation behavior.
