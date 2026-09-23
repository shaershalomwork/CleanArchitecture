# Optional database examples

These projects are outside the default solution. Each provider references Infrastructure and owns its driver, Dapper, connections, error classification, and health check. Infrastructure uses existing source interfaces and built-in keyed dependency injection to select the explicitly registered provider.

Start with [the database walkthrough](../../docs/template-guide.html#database). Add one project reference to Web and its `builder.Services.Add...CustomerRegistry()` call, then configure Provider, Live mode, and the named connection string. New applications can use `--CustomerProvider` to add the chosen implementation.

`Databases.slnx` and `Database.Tests` include all example providers for maintainer verification; ordinary builds do not reference them. Run `pwsh build/verify.ps1 -DatabaseExamples` for SQLite and classification tests, or `-SourceIntegration` to also start disposable SQL Server and Oracle fixtures. The latter requires Docker.

Startup never executes fixture scripts. They belong only to disposable examples. Existing transactions, error codes, cancellation, read retries, and uncertain write outcomes are preserved.
