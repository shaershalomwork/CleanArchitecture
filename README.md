# Data-Centric Integration Baseline

An organizational integration template derived from Jason Taylor's Clean Architecture.
External systems remain the systems of record. C# use cases combine narrow SQL/HTTP capabilities and return explicit Success, Warning or Error outcomes.

## Requirements

.NET SDK 10.0.400 (global.json). Node 24 for Angular. Docker only for the opt-in SQL fixture tests.
The default generated project is API-only; Angular is the only supported frontend.

## Develop this repository

```powershell
dotnet build
dotnet run --project src/Web --launch-profile https
```

Development uses fake sources and demo cookie authentication. Open /auth/login?returnUrl=/scalar to sign in.
Try GET /api/customers/{customerId}/overview with CUST-001 (complete), CUST-WARN (partial billing), CUST-FAIL (required source unavailable), or CUST-MISSING (404).
Web runs without Aspire, a database, or corporate credentials. Optional AppHost provides the diagnostics dashboard.

## Verify and package

```powershell
pwsh build/verify.ps1
pwsh build/verify.ps1 -SourceIntegration
pwsh build/test.ps1 -BrowserTests
pwsh build/test.ps1 -CustomerProvider SQLite -BrowserTests
```

The first command is offline with respect to corporate sources; package restore still requires your NuGet feed.
The second starts a disposable SQL Server using Docker. The last two commands cover all four provider/frontend combinations in isolated template hives, including generated use cases, publishing, HTTP smoke tests for every published app, and Angular browser tests with sign-in and sign-out.
Published smoke tests use Development authentication and fake sources. Real SQLite source/HTTP tests run in the offline suite; the disposable SQL Server contracts run with SourceIntegration. Corporate identity-provider and upstream connectivity require deployment-specific verification.
Do not point fixture tests at corporate databases.
For local browser verification with an installed Chrome or Edge, set PLAYWRIGHT_BROWSER_CHANNEL to chrome or msedge. CI uses Playwright's pinned Chromium.

```powershell
pwsh build/repack.ps1 -Version 0.1.0
dotnet new install ./artifacts/template-packages/DataCentric.Integration.Solution.Template.0.1.0.nupkg
dotnet new di-sln -n MyIntegration -cf Angular
```

Use `-cf None` (the default) for API-only. The former single-database option and React frontend are removed.
The customer source supports SQL Server (default) and SQLite. Generate the SQLite variant with `dotnet new di-sln -n MyIntegration -cf Angular --CustomerProvider SQLite`; see [SQLite setup](docs/sqlite.md). Development still starts with fake sources until a live source is configured.

## Architecture and operations

- [Interactive HTML handbook](docs/template-guide.html) — an offline, illustrated guide to setup, architecture, feature development, source integration, testing, and maintenance. Open the file in a browser.
- [Architecture decision](docs/decisions/ADR-004-Data-Centric-Integration.md)
- [Integration guide and source contracts](docs/integration-guide.md)
- [Dependencies and licensing](docs/dependencies.md)
- [Maintenance and rollout](docs/template-maintenance.md)

The baseline retains MediatR 14.2.0. Verify production entitlement for every consuming team; template MIT licensing does not cover third-party commercial licensing.
Store its key in configuration as MediatR:LicenseKey, never in source control.

Copyright notices from the original template are retained in LICENSE.
