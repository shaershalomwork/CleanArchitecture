using System.Net;
using System.Text.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
namespace CleanArchitecture.Application.FunctionalTests.Customers;
public class CustomerApiTests
{
    private WebApiFactory _factory = null!;
    private HttpClient _client = null!;
    [SetUp] public void Setup()
    {
        _factory = new();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
    }
    [TearDown] public void Dispose() { _client.Dispose(); _factory.Dispose(); }
    [TestCase("CUST-001", 200, "Success")]
    [TestCase("CUST-WARN", 200, "Warning")]
    [TestCase("CUST-MISSING", 404, "Error")]
    [TestCase("CUST-FAIL", 503, "Error")]
    [TestCase("invalid_id", 400, "Error")]
    public async Task ReturnsDocumentedContract(string id, int status, string outcome)
    {
        _client.DefaultRequestHeaders.Add("X-Test-User", "reader");
        var response = await _client.GetAsync("/api/customers/" + id + "/overview");
        ((int)response.StatusCode).ShouldBe(status);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetString().ShouldBe(outcome);
        var trace = json.RootElement.GetProperty("correlationId").GetString();
        response.Headers.GetValues("X-Correlation-ID").Single().ShouldBe(trace);
        if (outcome == "Warning")
        {
            json.RootElement.GetProperty("data").GetProperty("billingAvailable").GetBoolean().ShouldBeFalse();
            json.RootElement.GetProperty("data").GetProperty("outstandingBalance").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        if (outcome == "Error") json.RootElement.GetProperty("data").ValueKind.ShouldBe(JsonValueKind.Null);
    }
    [Test] public async Task RequiresAuthentication() =>
        (await _client.GetAsync("/api/customers/CUST-001/overview")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    [Test] public async Task RequiresPermission()
    {
        _client.DefaultRequestHeaders.Add("X-Test-User", "without-permission");
        (await _client.GetAsync("/api/customers/CUST-001/overview")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
    [Test] public async Task OpenApiAndLivenessAreAvailableWithoutCorporateConnections()
    {
        (await _client.GetAsync("/alive")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.GetAsync("/openapi/v1.json")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
