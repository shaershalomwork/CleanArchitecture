# SQLite customer source

SQL Server remains the default. SQLite is available in both Angular and API-only templates:

```powershell
dotnet new di-sln -n MyIntegration -cf Angular --CustomerProvider SQLite
```

An existing project can select SQLite with `Sources:CustomerRegistry:Provider=SQLite`.
Development starts with fake adapters regardless of provider. To use a real SQLite file, supply these environment settings before running Web:

```powershell
$env:Sources__CustomerRegistry__Mode = 'Live'
$env:Sources__CustomerRegistry__Provider = 'SQLite'
$env:ConnectionStrings__CustomerRegistry = 'Data Source=C:/data/customer-registry.db'
dotnet run --project src/Web --launch-profile https
```

Use an absolute path appropriate to your OS. Authentication and the optional billing source keep their existing configuration.
The database must exist and expose `Customers(Id TEXT PRIMARY KEY, DisplayName TEXT NOT NULL)`.
The adapter performs a parameterized point query; SQLite does not provide SQL Server stored procedures.
For a disposable local example, run [customer-registry.sqlite.sql](customer-registry.sqlite.sql) against a new file using a SQLite client. The application never initializes or migrates source databases.

Lookup connections are read-only, even if a supplied connection string asks for write/create mode. The separate write adapter uses ReadWrite connections and never creates a missing file. A missing file on a read returns `CUSTOMER.UNAVAILABLE`; a missing customer returns `CUSTOMER.NOT_FOUND`. Invalid data and schema failures return an error, never an empty successful result. See [customer write examples](customer-writes.md) for mutations and unknown write outcomes.
The shared executor emits the same correlation, latency and outcome telemetry as SQL Server. SQLite is an in-process source, so it has no server session context; correlation stays in application traces and result issues.

Microsoft.Data.Sqlite uses synchronous I/O and internally waits/retries on busy locks. The adapter offloads its small point lookup and bounds lock waiting with the attempt timeout, rounded up to whole seconds. Caller cancellation is checked before opening and after reading. An in-progress native call cannot be promised an immediate hard abort; allow for that granularity when tuning request budgets. See Microsoft's [async limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async) and [locking/timeouts](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors).

SQLite tests use disposable files, run in the normal offline suite, and need no Docker:

```powershell
dotnet test tests/Infrastructure.IntegrationTests --filter FullyQualifiedName~SqliteCustomerTests
pwsh build/test.ps1 -CustomerProvider SQLite -BrowserTests
```
