using Aspire.Hosting;
using Aspire.Hosting.Testing;
using CleanArchitecture.Application.Common.Results;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Connections;
using CleanArchitecture.Infrastructure.IntegrationTests.Execution;
using CleanArchitecture.Infrastructure.Sources.CustomerRegistry;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Shouldly;
namespace CleanArchitecture.Infrastructure.IntegrationTests.Sources;

[Category("SourceIntegration")]
public class SqlCustomerTests
{
    private DistributedApplication? _host;
    private IConfiguration _configuration = null!;
    [OneTimeSetUp] public async Task StartDisposableFixture()
    {
        if (Environment.GetEnvironmentVariable("RUN_SOURCE_INTEGRATION_TESTS") != "1")
            Assert.Ignore("Set RUN_SOURCE_INTEGRATION_TESTS=1 to run the disposable SQL Server fixture (Docker required).");
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.TestAppHost>(
            args: [], configureBuilder: (settings, _) => settings.DisableDashboard = true);
        builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";
        _host = await builder.BuildAsync(cts.Token);
        await _host.StartAsync(cts.Token);
        await _host.ResourceNotifications.WaitForResourceHealthyAsync("customer-registry", cts.Token);
        var connectionString = await _host.GetConnectionStringAsync("customer-registry", cts.Token);
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CustomerRegistry"] = connectionString,
            ["ConnectionStrings:SecondName"] = connectionString
        }).Build();
        await using var connection = new SqlConnection(connectionString);
        // Only the connection returned by this disposable host is ever initialized.
        await connection.OpenAsync(cts.Token);
        foreach (var batch in (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures/customer-registry.sql"), cts.Token))
            .Replace("\r\n", "\n", StringComparison.Ordinal).Split("\nGO\n", StringSplitOptions.RemoveEmptyEntries))
            await connection.ExecuteAsync(batch);
    }
    [OneTimeTearDown] public async Task StopFixture() { if (_host is not null) await _host.DisposeAsync(); }
    [TestCase("CUST-001", OperationStatus.Success)]
    [TestCase("CUST-MISSING", OperationStatus.Error)]
    public async Task CallsStoredProcedureAndInterpretsReturnCode(string id, OperationStatus expected)
    {
        var correlation = new SourceExecutorTests.Correlation();
        var adapter = new SqlCustomerSourceAdapter(new(_configuration, correlation), SourceExecutorTests.Create(),
            Options.Create(new CustomerRegistryOptions()), Options.Create(new SourceExecutionOptions()), correlation, TimeProvider.System);
        var result = await adapter.GetCustomerAsync(id, default);
        result.Status.ShouldBe(expected);
        if (expected == OperationStatus.Success) result.Data.DisplayName.ShouldBe("Fixture Customer");
        else result.Issues[0].Code.ShouldBe("CUSTOMER.NOT_FOUND");
    }
    [Test] public async Task NamedConnectionsCarryAndClearCorrelation()
    {
        var factory = new SqlConnectionFactory(_configuration, new SourceExecutorTests.Correlation());
        await using (var lease = await factory.OpenAsync("SecondName", default))
            (await lease.Connection.ExecuteScalarAsync<string>("SELECT CONVERT(nvarchar(100), SESSION_CONTEXT(N'CorrelationId'))")).ShouldBe("test-trace");
        var pooledSettings = new SqlConnectionStringBuilder(_configuration.GetConnectionString("SecondName")) { ConnectRetryCount = 0 };
        await using var connection = new SqlConnection(pooledSettings.ConnectionString);
        await connection.OpenAsync();
        (await connection.ExecuteScalarAsync<string?>("SELECT CONVERT(nvarchar(100), SESSION_CONTEXT(N'CorrelationId'))")).ShouldBeNull();
    }
}
