using CleanArchitecture.Application.Common.Results;
using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Connections;
using CleanArchitecture.Infrastructure.IntegrationTests.Execution;
using CleanArchitecture.Infrastructure.Observability;
using CleanArchitecture.Infrastructure.Sources.CustomerRegistry;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CleanArchitecture.Infrastructure.IntegrationTests.Sources;

public class SqliteCustomerTests
{
    private string _path = null!;
    private IConfiguration _configuration = null!;

    [SetUp] public void CreateDisposableSource()
    {
        _path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N") + ".db");
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CustomerRegistry"] = new SqliteConnectionStringBuilder { DataSource = _path }.ConnectionString,
            ["ConnectionStrings:Missing"] = new SqliteConnectionStringBuilder { DataSource = _path + ".missing" }.ConnectionString
        }).Build();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ConnectionString);
        connection.Open();
        connection.Execute("CREATE TABLE Customers (Id TEXT, DisplayName TEXT); INSERT INTO Customers VALUES ('CUST-001', 'SQLite Customer'), ('CUST-INVALID', '');");
    }

    [TearDown] public void RemoveDisposableSource()
    {
        // All paths belong to this fixture; never accept an external database path.
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    private SqliteCustomerSourceAdapter Create(string name = "CustomerRegistry") => new(
        new(_configuration, Options.Create(new SourceExecutionOptions())), SourceExecutorTests.Create(),
        Options.Create(new CustomerRegistryOptions { Provider = CustomerDatabaseProvider.SQLite, ConnectionName = name }),
        new SourceExecutorTests.Correlation(), TimeProvider.System);

    [Test] public async Task ReadsCustomerAndMapsItsContract()
    {
        var result = await Create().GetCustomerAsync("CUST-001", default);
        result.Status.ShouldBe(OperationStatus.Success);
        result.Data.DisplayName.ShouldBe("SQLite Customer");
    }

    [TestCase("CUST-MISSING", "CUSTOMER.NOT_FOUND")]
    [TestCase("CUST-INVALID", "CUSTOMER.INVALID_RESPONSE")]
    [TestCase("' OR 1=1 --", "CUSTOMER.NOT_FOUND")]
    public async Task PreservesFailuresAndBindsParameters(string id, string code)
    {
        var result = await Create().GetCustomerAsync(id, default);
        result.HasData.ShouldBeFalse();
        result.Issues[0].Code.ShouldBe(code);
        result.Issues[0].CorrelationId.ShouldBe("test-trace");
    }

    [Test] public async Task MissingSourceIsAnErrorAndIsNeverCreated()
    {
        var result = await Create("Missing").GetCustomerAsync("CUST-001", default);
        result.Issues[0].Code.ShouldBe("CUSTOMER.UNAVAILABLE");
        File.Exists(_path + ".missing").ShouldBeFalse();
    }

    [Test] public void ConnectionsAreReadOnly()
    {
        using var connection = new SqliteConnectionFactory(_configuration, Options.Create(new SourceExecutionOptions()))
            .Open("CustomerRegistry", default);
        Should.Throw<SqliteException>(() => connection.Execute("DELETE FROM Customers"));
    }

    [Test] public void CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Should.ThrowAsync<OperationCanceledException>(() => Create().GetCustomerAsync("CUST-001", cancellation.Token));
    }

    [Test] public async Task ConfigurationSelectsSqliteAndItsReadinessProbe()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Test" });
        builder.Configuration.AddConfiguration(_configuration);
        builder.Configuration["Sources:CustomerRegistry:Provider"] = "SQLite";
        builder.Configuration["Sources:Billing:Mode"] = "Fake";
        builder.AddInfrastructureServices();
        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICustomerSourceAdapter>().ShouldBeOfType<SqliteCustomerSourceAdapter>();
        var probe = ActivatorUtilities.CreateInstance<CustomerRegistryHealthCheck>(scope.ServiceProvider);
        (await probe.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Healthy);
    }
}
