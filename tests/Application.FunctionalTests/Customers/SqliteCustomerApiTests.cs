using System.Text.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace CleanArchitecture.Application.FunctionalTests.Customers;

public class SqliteCustomerApiTests
{
    [TestCase("CUST-001", 200, "Success")]
    [TestCase("CUST-WARN", 200, "Warning")]
    [TestCase("CUST-MISSING", 404, "Error")]
    public async Task ServesRealSqliteDataThroughTheHttpWorkflow(string id, int status, string outcome)
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N") + ".db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString;
        try
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE Customers (Id TEXT PRIMARY KEY, DisplayName TEXT NOT NULL); INSERT INTO Customers VALUES ('CUST-001', 'SQLite Customer'), ('CUST-WARN', 'SQLite Customer');";
                command.ExecuteNonQuery();
            }
            await using var baseline = new WebApiFactory();
            await using var factory = baseline.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, c) =>
                c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Sources:CustomerRegistry:Mode"] = "Live",
                    ["Sources:CustomerRegistry:Provider"] = "SQLite",
                    ["ConnectionStrings:CustomerRegistry"] = connectionString
                })));
            using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost") });
            client.DefaultRequestHeaders.Add("X-Test-User", "reader");
            var response = await client.GetAsync("/api/customers/" + id + "/overview");
            ((int)response.StatusCode).ShouldBe(status);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("status").GetString().ShouldBe(outcome);
            if (status == 200)
            {
                var data = json.RootElement.GetProperty("data");
                data.GetProperty("displayName").GetString().ShouldBe("SQLite Customer");
                data.GetProperty("billingAvailable").GetBoolean().ShouldBe(outcome == "Success");
            }
            else json.RootElement.GetProperty("data").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
