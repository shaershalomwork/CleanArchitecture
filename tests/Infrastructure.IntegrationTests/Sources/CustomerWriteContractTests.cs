using System.Data.Common;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using CleanArchitecture.Application.Common.Results;
using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Connections;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Oracle.ManagedDataAccess.Client;
using Shouldly;

namespace CleanArchitecture.Infrastructure.IntegrationTests.Sources;

[TestFixture(CustomerDatabaseProvider.SQLite)]
[TestFixture(CustomerDatabaseProvider.SqlServer, Category = "SourceIntegration")]
[TestFixture(CustomerDatabaseProvider.Oracle, Category = "SourceIntegration")]
public class CustomerWriteContractTests(CustomerDatabaseProvider provider)
{
    private DistributedApplication? _fixture;
    private IHost? _services;
    private string? _path;
    private string _connectionString = "";

    [OneTimeSetUp] public async Task StartFixture()
    {
        if (provider != CustomerDatabaseProvider.SQLite && Environment.GetEnvironmentVariable("RUN_SOURCE_INTEGRATION_TESTS") != "1")
            Assert.Ignore("Set RUN_SOURCE_INTEGRATION_TESTS=1 to run disposable database fixtures (Docker required).");
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        if (provider == CustomerDatabaseProvider.SQLite)
        {
            _path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N") + ".db");
            _connectionString = new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ConnectionString;
        }
        else
        {
            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.TestAppHost>(
                args: ["FixtureProvider=" + provider], configureBuilder: (settings, _) => settings.DisableDashboard = true);
            builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";
            _fixture = await builder.BuildAsync(budget.Token);
            await _fixture.StartAsync(budget.Token);
            await _fixture.ResourceNotifications.WaitForResourceHealthyAsync("customer-registry", budget.Token);
            _connectionString = (await _fixture.GetConnectionStringAsync("customer-registry", budget.Token))!;
        }
        // Only the connection created by this fixture can be initialized.
        await using (var connection = NewConnection())
        {
            await connection.OpenAsync(budget.Token);
            if (provider == CustomerDatabaseProvider.SQLite)
                await connection.ExecuteAsync("CREATE TABLE Customers (Id TEXT PRIMARY KEY, DisplayName TEXT NOT NULL); INSERT INTO Customers VALUES ('CUST-001', 'Fixture Customer');");
            else
            {
                var file = provider == CustomerDatabaseProvider.Oracle ? "customer-registry.oracle.sql" : "customer-registry.sql";
                var script = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", file), budget.Token))
                    .Replace("\r\n", "\n", StringComparison.Ordinal);
                var batches = provider == CustomerDatabaseProvider.Oracle
                    ? script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : script.Split("\nGO\n", StringSplitOptions.RemoveEmptyEntries);
                foreach (var batch in batches)
                    await connection.ExecuteAsync(new CommandDefinition(batch, cancellationToken: budget.Token));
            }
        }
        var services = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Test" });
        services.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sources:CustomerRegistry:Provider"] = provider.ToString(), ["Sources:CustomerRegistry:ConnectionName"] = "SecondName",
            ["ConnectionStrings:SecondName"] = _connectionString, ["Sources:Billing:Mode"] = "Fake"
        });
        services.AddInfrastructureServices();
        _services = services.Build();
    }

    [OneTimeTearDown] public async Task Cleanup()
    {
        _services?.Dispose();
        if (_fixture is not null) await _fixture.DisposeAsync();
        if (_path is not null) { SqliteConnection.ClearAllPools(); File.Delete(_path); }
    }

    private DbConnection NewConnection() => provider switch
    {
        CustomerDatabaseProvider.SQLite => new SqliteConnection(_connectionString),
        CustomerDatabaseProvider.SqlServer => new SqlConnection(_connectionString),
        _ => new OracleConnection(_connectionString)
    };

    [Test] public async Task RoundTripsWritesAndMapsBusinessFailures()
    {
        using var scope = _services!.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<ICustomerWriteSourceAdapter>();
        var reader = scope.ServiceProvider.GetRequiredService<ICustomerSourceAdapter>();
        var id = "WRITE-" + Guid.NewGuid().ToString("N");
        var name = "שלום O'Brien :CustomerId @DisplayName";
        (await writer.CreateAsync(id, name, default)).Status.ShouldBe(OperationStatus.Success);
        (await reader.GetCustomerAsync(id, default)).Data.DisplayName.ShouldBe(name);
        (await writer.CreateAsync(id, "Duplicate", default)).Issues[0].Code.ShouldBe("CUSTOMER.CONFLICT");
        (await reader.GetCustomerAsync(id, default)).Data.DisplayName.ShouldBe(name);
        (await writer.ReplaceAsync(id, "Replace", default)).Status.ShouldBe(OperationStatus.Success);
        (await writer.PatchAsync(id, "Patch", default)).Status.ShouldBe(OperationStatus.Success);
        var read = await reader.GetCustomerAsync(id, default);
        read.Data.Id.ShouldBe(id);
        read.Data.DisplayName.ShouldBe("Patch");
        (await writer.DeleteAsync(id, default)).Status.ShouldBe(OperationStatus.Success);
        (await reader.GetCustomerAsync(id, default)).Issues[0].Code.ShouldBe("CUSTOMER.NOT_FOUND");
        (await writer.DeleteAsync(id, default)).Issues[0].Code.ShouldBe("CUSTOMER.NOT_FOUND");
        (await writer.ReplaceAsync(id, "Missing", default)).Issues[0].Code.ShouldBe("CUSTOMER.NOT_FOUND");
        (await writer.PatchAsync(id, "Missing", default)).Issues[0].Code.ShouldBe("CUSTOMER.NOT_FOUND");
    }

    [Test] public async Task SourceQueriesBindIdsAndObserveCallerCancellation()
    {
        using var scope = _services!.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ICustomerSourceAdapter>();
        (await reader.GetCustomerAsync("' OR 1=1 --", default)).Issues[0].Code.ShouldBe("CUSTOMER.NOT_FOUND");
        var writer = scope.ServiceProvider.GetRequiredService<ICustomerWriteSourceAdapter>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => writer.CreateAsync("CANCEL", "Name", cancelled.Token));
        (await reader.GetCustomerAsync("CANCEL", default)).Issues[0].Code.ShouldBe("CUSTOMER.NOT_FOUND");
    }

    [Test] public async Task ConcurrentCreatesHaveExactlyOneWinner()
    {
        var id = "RACE-" + Guid.NewGuid().ToString("N");
        using var first = _services!.Services.CreateScope();
        using var second = _services.Services.CreateScope();
        var results = await Task.WhenAll(
            first.ServiceProvider.GetRequiredService<ICustomerWriteSourceAdapter>().CreateAsync(id, "First", default),
            second.ServiceProvider.GetRequiredService<ICustomerWriteSourceAdapter>().CreateAsync(id, "Second", default));
        results.Count(r => r.HasData).ShouldBe(1);
        results.Single(r => !r.HasData).Issues[0].Code.ShouldBe("CUSTOMER.CONFLICT");
    }

    [Test] public async Task ConfiguredProviderHasHealthyReadiness()
    {
        var checks = _services!.Services.GetRequiredService<HealthCheckService>();
        (await checks.CheckHealthAsync(r => r.Name == "CustomerRegistry")).Status.ShouldBe(HealthStatus.Healthy);
    }

    [Test] public async Task OracleConnectionsBindByNameAndClearCorrelationOnDisposal()
    {
        if (provider != CustomerDatabaseProvider.Oracle) return;
        using var scope = _services!.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<OracleConnectionFactory>();
        await using (var connection = await factory.OpenAsync("SecondName", default))
        {
            connection.BindByName.ShouldBeTrue();
            var clientId = await connection.ExecuteScalarAsync<string>("SELECT SYS_CONTEXT('USERENV', 'CLIENT_IDENTIFIER') FROM DUAL");
            clientId.ShouldNotBeNullOrWhiteSpace();
        }
        await using var pooled = NewConnection();
        await pooled.OpenAsync();
        (await pooled.ExecuteScalarAsync<string?>("SELECT SYS_CONTEXT('USERENV', 'CLIENT_IDENTIFIER') FROM DUAL")).ShouldBeNullOrEmpty();
    }
}
