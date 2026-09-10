using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace CleanArchitecture.Application.FunctionalTests.Customers;

[TestFixture(false)]
[TestFixture(true)]
public class CustomerWriteApiTests(bool sqlite)
{
    private WebApiFactory _baseline = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string? _path;

    [SetUp] public async Task Setup()
    {
        _baseline = new();
        _factory = _baseline;
        if (sqlite)
        {
            _path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N") + ".db");
            var connectionString = new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ConnectionString;
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                connection.Execute("CREATE TABLE Customers (Id TEXT PRIMARY KEY, DisplayName TEXT NOT NULL); INSERT INTO Customers VALUES ('CUST-001', 'Example Customer');");
            }
            _factory = _baseline.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Sources:CustomerRegistry:Mode"] = "Live", ["Sources:CustomerRegistry:Provider"] = "SQLite",
                    ["ConnectionStrings:CustomerRegistry"] = connectionString
                })));
        }
        _client = _factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        _client.DefaultRequestHeaders.Add("X-Test-User", "reader-writer");
        var csrf = await _client.GetFromJsonAsync<JsonElement>("/auth/antiforgery");
        _client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    [TearDown] public async Task Cleanup()
    {
        _client.Dispose();
        if (!ReferenceEquals(_factory, _baseline)) await _factory.DisposeAsync();
        await _baseline.DisposeAsync();
        if (_path is not null) { SqliteConnection.ClearAllPools(); File.Delete(_path); }
    }

    private async Task<JsonElement> Send(string method, string path, string? body, int status)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        ((int)response.StatusCode).ShouldBe(status, raw);
        var json = JsonSerializer.Deserialize<JsonElement>(raw);
        json.GetProperty("status").GetString().ShouldBe(status == 200 ? "Success" : "Error");
        var trace = json.GetProperty("correlationId").GetString();
        response.Headers.GetValues("X-Correlation-ID").Single().ShouldBe(trace);
        if (status != 200)
        {
            json.GetProperty("data").ValueKind.ShouldBe(JsonValueKind.Null);
            foreach (var issue in json.GetProperty("issues").EnumerateArray())
                issue.GetProperty("correlationId").GetString().ShouldBe(trace);
        }
        return json;
    }

    [Test] public async Task WritesAreVisibleAcrossRequestsAndDeletionIsNotResurrected()
    {
        const string route = "/api/customers/WRITE-1";
        var created = await Send("POST", "/api/customers", """{"customerId":"WRITE-1","displayName":"שלום"}""", 200);
        created.GetProperty("data").GetProperty("customerId").GetString().ShouldBe("WRITE-1");
        created.GetProperty("data").GetProperty("observedAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
        await Send("POST", "/api/customers", """{"customerId":"WRITE-1","displayName":"Duplicate"}""", 409);
        await Send("PUT", route, """{"displayName":"Replaced"}""", 200);
        await Send("PATCH", route, """{"displayName":"Patched"}""", 200);
        var read = await Send("GET", route + "/overview", null, 200);
        read.GetProperty("data").GetProperty("displayName").GetString().ShouldBe("Patched");
        read.GetProperty("data").GetProperty("customerId").GetString().ShouldBe("WRITE-1");
        var deleted = await Send("DELETE", route, null, 200);
        deleted.GetProperty("data").GetProperty("customerId").GetString().ShouldBe("WRITE-1");
        await Send("GET", route + "/overview", null, 404);
        await Send("DELETE", route, null, 404);
        await Send("PUT", route, """{"displayName":"Missing"}""", 404);
        await Send("PATCH", route, """{"displayName":"Missing"}""", 404);
    }

    [TestCase("PATCH", "{}")]
    [TestCase("PATCH", "{\"displayName\":null}")]
    [TestCase("PATCH", "{\"displayName\":\" \"}")]
    [TestCase("PATCH", "{\"displayName\":\"Changed\",\"customerId\":\"NEW\"}")]
    [TestCase("PATCH", "{\"unknown\":true}")]
    [TestCase("PATCH", "[{\"op\":\"replace\",\"path\":\"/displayName\",\"value\":\"Changed\"}]")]
    [TestCase("PATCH", "{")]
    [TestCase("PATCH", "null")]
    [TestCase("PUT", "{}")]
    public async Task InvalidBodiesDoNotModifyTheCustomer(string method, string body)
    {
        await Send(method, "/api/customers/CUST-001", body, 400);
        var read = await Send("GET", "/api/customers/CUST-001/overview", null, 200);
        read.GetProperty("data").GetProperty("displayName").GetString().ShouldBe("Example Customer");
    }

    [Test] public async Task EnforcesLengthAndIdentifierRules()
    {
        await Send("POST", "/api/customers", JsonSerializer.Serialize(new { customerId = "bad_id", displayName = "Name" }), 400);
        await Send("POST", "/api/customers", JsonSerializer.Serialize(new { customerId = new string('A', 51), displayName = "Name" }), 400);
        await Send("POST", "/api/customers", JsonSerializer.Serialize(new { customerId = "LONG", displayName = new string('N', 201) }), 400);
        await Send("POST", "/api/customers", JsonSerializer.Serialize(new { customerId = new string('A', 50), displayName = new string('N', 200) }), 200);
    }

    [TestCase("POST")]
    [TestCase("PUT")]
    [TestCase("PATCH")]
    [TestCase("DELETE")]
    public async Task RequiresWritePermissionAndCsrf(string method)
    {
        var route = method == "POST" ? "/api/customers" : "/api/customers/CUST-001";
        var body = method == "POST" ? "{\"customerId\":\"AUTH-1\",\"displayName\":\"Name\"}" : "{\"displayName\":\"Name\"}";
        _client.DefaultRequestHeaders.Remove("X-Test-User");
        await Send(method, route, body, 401);
        _client.DefaultRequestHeaders.Add("X-Test-User", "reader");
        await Send(method, route, body, 403);
        _client.DefaultRequestHeaders.Remove("X-Test-User");
        _client.DefaultRequestHeaders.Add("X-Test-User", "writer");
        _client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        await Send(method, route, body, 400);
        var csrf = await _client.GetFromJsonAsync<JsonElement>("/auth/antiforgery");
        _client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        await Send(method, route, body, 200);
        await Send("GET", "/api/customers/CUST-001/overview", null, 403);
    }

    [Test] public async Task FakeWriteFailureReportsUnknownOutcome()
    {
        if (sqlite) Assert.Ignore("Deterministic fake fault scenario.");
        var response = await Send("PATCH", "/api/customers/CUST-FAIL", """{"displayName":"Name"}""", 502);
        response.GetProperty("issues")[0].GetProperty("code").GetString().ShouldBe("CUSTOMER.OUTCOME_UNKNOWN");
    }
}
