using CleanArchitecture.Application.FunctionalTests.Customers;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Database.Tests.Api;

public sealed class SqliteWebApiFactory : WebApiFactory
{
    private readonly string _path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N") + ".db");
    private readonly string _connectionString;

    public SqliteWebApiFactory()
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ConnectionString;
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Customers (Id TEXT PRIMARY KEY, DisplayName TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sources:CustomerRegistry:Mode"] = "Live", ["Sources:CustomerRegistry:Provider"] = "SQLite",
            ["ConnectionStrings:CustomerRegistry"] = _connectionString
        }));
        builder.ConfigureTestServices(services => services.AddSqliteCustomerRegistry());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }
}

public sealed class SqliteCustomerWriteApiTests : CustomerWriteApiContractTests
{
    protected override WebApplicationFactory<Program> CreateFactory() => new SqliteWebApiFactory();
}

public sealed class SqliteCustomerListApiTests : CustomerListApiContractTests
{
    protected override WebApplicationFactory<Program> CreateFactory() => new SqliteWebApiFactory();
}
