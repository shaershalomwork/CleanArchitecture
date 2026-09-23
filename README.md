# Organizational application template

A .NET application starting point with customer APIs, validation, authentication, and tests. Development starts with an empty in-memory registry and simulated billing; the base application has no database driver dependency.

**Start with the [developer guide](docs/template-guide.html).** It covers purpose, first run, adding an API, and connecting a database with parameterized query and stored-procedure examples.

~~~powershell
dotnet dev-certs https --trust
dotnet run --project src/Web --launch-profile https
~~~

Use the HTTPS address printed by Web. The guide includes sign-in and sample CRUD calls. Sample data belongs to one process and disappears on restart. Fake sources are restricted to Development, including automated hosts that previously used an environment named Test.

## Maintainer commands

Use the SDK selected by `global.json` and PowerShell 7.

~~~powershell
pwsh build/verify.ps1
pwsh build/verify.ps1 -DatabaseExamples
pwsh build/verify.ps1 -SourceIntegration
pwsh build/test.ps1 -CustomerProvider None -BrowserTests
~~~

Default verification has no database or Docker requirement. Optional database verification uses disposable SQLite files; SourceIntegration also starts disposable SQL Server and Oracle containers.

Database implementations, readiness probes, and fixtures live under `examples/Databases`, outside the default solution and dependency graph. Provider generation choices reference only the selected implementation.

## Package and generate

~~~powershell
pwsh build/repack.ps1
dotnet new install ./artifacts/template-packages/DataCentric.Integration.Solution.Template.0.1.0.nupkg
dotnet new di-sln -n MyApplication -cf None
~~~

`-cf None` and `--CustomerProvider None` are defaults. Optional provider choices are SQLite, SqlServer, and Oracle; Development still uses simulated sources until Live is selected. Use `-cf Angular` for a browser application, which additionally needs Node 24. API-only development runs Web directly; AppHost remains an optional Aspire launcher.

The package matrix covers all four provider choices and both clients. See [maintenance and release](docs/template-maintenance.md), [dependency inventory](docs/dependencies.md), and [architecture decisions](docs/decisions/README.md).

## Provenance

Derived from [Jason Taylor's Clean Architecture](https://github.com/jasontaylordev/CleanArchitecture), with organizational adaptations. The repository retains its MIT [license](LICENSE). Generated applications record their baseline in `.template-version.json` and evolve independently.
