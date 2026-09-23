# Application template

A starting structure for organizational applications with consistent APIs, validation, authentication, and tests. The customer workflow is a working example.

Start with the [developer guide](docs/template-guide.html): purpose, initial setup, adding an API, and connecting a database.

## Run without a database

Install a .NET SDK compatible with `global.json`, then run from the application root:

~~~powershell
dotnet dev-certs https --trust
dotnet run --project src/Web --launch-profile https
~~~

Use the HTTPS address printed by Web and open `/scalar`. Development uses an empty in-memory customer registry and simulated billing. Changes disappear on restart. Sign in using `/auth/login?profile=reader-writer&returnUrl=/scalar` to exercise writes; the guide includes cookie and antiforgery examples.

Fake sources are permitted only in Development. Automated test hosts also use Development; an environment named Test does not permit Fake sources. Deployments require Live sources and external authentication.

## Add a database when needed

The default application has no database driver or Dapper dependency. Optional examples live under `examples/Databases`:

| Provider | Project | Registration on builder.Services |
| --- | --- | --- |
| SQLite | SQLite/SQLite.csproj | AddSqliteCustomerRegistry() |
| SqlServer | SqlServer/SqlServer.csproj | AddSqlServerCustomerRegistry() |
| Oracle | Oracle/Oracle.csproj | AddOracleCustomerRegistry() |

Add the chosen project reference and registration, then configure the named connection string, Provider, and Live mode. Alternatively choose `--CustomerProvider SQLite`, `SqlServer`, or `Oracle` when generating a new application. The [guide](docs/template-guide.html#database) includes local SQLite setup, parameterized SQL, and stored-procedure execution.

## Verify and extend

~~~powershell
pwsh build/verify.ps1
pwsh build/verify.ps1 -DatabaseExamples
pwsh build/verify.ps1 -SourceIntegration
~~~

The first command needs no database or Docker. The second builds the optional examples and runs local SQLite/provider tests. The third also starts disposable SQL Server and Oracle fixtures and requires Docker. Use PowerShell 7.

Follow the guide's complete API tutorial, or run `dotnet new di-usecase` inside `src/Application` after installing the matching package. Preserve existing HTTP contracts and approve intentional new operations in the saved API contract.

Angular applications additionally need Node 24: build Web, run `npm --prefix src/Web/ClientApp ci`, then `dotnet run --project src/AppHost`. Aspire and external telemetry are optional for API-only applications.

See [API contracts](docs/customer-writes.md), [authentication](docs/workspace.md), [dependencies](docs/dependencies.md), and [template maintenance](docs/template-maintenance.md). Baseline provenance is recorded in `.template-version.json`.
