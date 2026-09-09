using System.Net;
using System.Text.Json;

namespace CleanArchitecture.Application.FunctionalTests.Customers;

[Category("Published")]
public class PublishedCustomerApiTests
{
    private HttpClient _client = null!;

    [SetUp] public void ConnectToPublishedApplication()
    {
        var address = Environment.GetEnvironmentVariable("TEST_BASE_URL");
        if (string.IsNullOrWhiteSpace(address)) Assert.Ignore("Set TEST_BASE_URL to the published Development application.");
        var uri = new Uri(address!);
        if (!uri.IsLoopback) throw new InvalidOperationException("Published smoke tests require a local disposable application.");
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = uri };
    }

    [TearDown] public void Dispose() => _client?.Dispose();

    [Test] public async Task RequiresAuthenticationAndPublishesOpenApi()
    {
        (await _client.GetAsync("/api/customers/CUST-001/overview")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var schema = JsonDocument.Parse(await _client.GetStringAsync("/openapi/v1.json"));
        schema.RootElement.GetProperty("paths").TryGetProperty("/api/customers/{customerId}/overview", out _).ShouldBeTrue();
    }

    [TestCase("CUST-001", 200, "Success")]
    [TestCase("CUST-WARN", 200, "Warning")]
    [TestCase("CUST-MISSING", 404, "Error")]
    [TestCase("CUST-FAIL", 503, "Error")]
    [TestCase("invalid_id", 400, "Error")]
    public async Task ServesPublishedContract(string id, int status, string outcome)
    {
        var login = await _client.GetAsync("/auth/login");
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        // Replay only this disposable host's development cookie over loopback HTTP.
        // Browser tests separately verify browser cookie handling and sign-out.
        var cookie = login.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0])
            .Single(value => value.StartsWith("__Host-Integration=", StringComparison.Ordinal));
        _client.DefaultRequestHeaders.Add("Cookie", cookie);
        var response = await _client.GetAsync("/api/customers/" + id + "/overview");
        ((int)response.StatusCode).ShouldBe(status);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement;
        result.GetProperty("status").GetString().ShouldBe(outcome);
        response.Headers.GetValues("X-Correlation-ID").Single().ShouldBe(result.GetProperty("correlationId").GetString());
        if (outcome == "Error") result.GetProperty("data").ValueKind.ShouldBe(JsonValueKind.Null);
        if (outcome == "Warning")
        {
            result.GetProperty("data").GetProperty("billingAvailable").GetBoolean().ShouldBeFalse();
            result.GetProperty("data").GetProperty("outstandingBalance").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }
}
