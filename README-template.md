# CleanArchitecture

Generated from DataCentric.Integration.Solution.Template version __BaselineVersion__, based on upstream commit 705d77f.
See .template-version.json for provenance.
Open the [interactive HTML handbook](docs/template-guide.html) in a browser for an offline, illustrated guide to the architecture and end-to-end development workflow.
The live customer source supports SQL Server and SQLite. See [SQLite setup](docs/sqlite.md) for the database contract and configuration.

## Run locally

Install the SDK selected by global.json. For Angular, install Node 24 as well.

```powershell
dotnet build
dotnet run --project src/Web --launch-profile https
```

Development uses fake sources and demo sign-in. API-only opens Scalar; sign in at /auth/login?returnUrl=/scalar.
For Angular, run `dotnet run --project src/AppHost` and open the frontend URL from the dashboard.
The fake IDs are CUST-001 (complete), CUST-WARN (partial), CUST-FAIL (required-source failure), CUST-MISSING (not found).

## Verify

```powershell
pwsh build/verify.ps1
pwsh build/verify.ps1 -SourceIntegration
```

The default suite requires no corporate connections or Docker. SourceIntegration starts a disposable SQL Server and requires Docker.
Browser tests require TEST_BASE_URL pointing to the published Development application and the Playwright browser installed.
Production configuration rejects fake sources and development authentication.

## Add a use case

Run inside src/Application:

```powershell
dotnet new di-usecase -n GetReport -fn Reports -ut query -rt NoData --RootNamespace CleanArchitecture.Application
dotnet new di-usecase -n SubmitReport -fn Reports -ut command --RootNamespace CleanArchitecture.Application
```

The return type is the payload of OperationResult<T>. Replace NoData with an Application model for data-returning queries.
The stub returns USE_CASE.NOT_IMPLEMENTED until real behavior is added. Inject narrow source interfaces, never a context or nested mediator.
Install the matching DataCentric.Integration.Solution.Template version if the item template is unavailable.

Read [the integration guide](docs/integration-guide.md), [licensing](docs/dependencies.md), and [upgrade policy](docs/template-maintenance.md) before connecting real systems.
