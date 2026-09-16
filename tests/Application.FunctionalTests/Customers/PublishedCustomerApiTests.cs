using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CleanArchitecture.Application.FunctionalTests.Customers;

[Category("Published")]
public class PublishedCustomerApiTests
{
    private HttpClient _client = null!;
    private HttpClient? _writer;
    private readonly List<string> _registered = [];

    [SetUp] public async Task ConnectToPublishedApplication()
    {
        _registered.Clear();
        var address = Environment.GetEnvironmentVariable("TEST_BASE_URL");
        if (string.IsNullOrWhiteSpace(address)) Assert.Ignore("Set TEST_BASE_URL to the published Development application.");
        var uri = new Uri(address!);
        if (!uri.IsLoopback) throw new InvalidOperationException("Published smoke tests require a local disposable application.");
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = uri };
        _writer = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = uri };
        var writerToken = Environment.GetEnvironmentVariable("TEST_WRITER_TOKEN");
        if (!string.IsNullOrWhiteSpace(writerToken)) _writer.DefaultRequestHeaders.Authorization = new("Bearer", writerToken);
        else
        {
            var login = await _writer.GetAsync("/auth/login?profile=reader-writer");
            login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            var cookies = login.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]).ToList();
            _writer.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));
            var csrf = await _writer.GetAsync("/auth/antiforgery");
            csrf.EnsureSuccessStatusCode();
            cookies.AddRange(csrf.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]));
            _writer.DefaultRequestHeaders.Remove("Cookie");
            _writer.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));
            var token = await csrf.Content.ReadFromJsonAsync<JsonElement>();
            _writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
        }
        foreach (var id in new[] { "CUST-001", "CUST-WARN" })
        {
            (await _writer.PostAsJsonAsync("/api/customers", new { customerId = id, displayName = "Example Customer" })).StatusCode.ShouldBe(HttpStatusCode.OK);
            _registered.Add(id);
        }
    }

    [TearDown] public async Task Dispose()
    {
        if (_writer is not null)
        {
            foreach (var id in _registered) await _writer.DeleteAsync("/api/customers/" + id);
            _writer.Dispose();
        }
        _client?.Dispose();
    }

    [Test] public async Task RequiresAuthenticationAndPublishesOpenApi()
    {
        (await _client.GetAsync("/api/customers/CUST-001/overview")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.GetAsync("/api/customers")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var schema = JsonDocument.Parse(await _client.GetStringAsync("/openapi/v1.json"));
        schema.RootElement.GetProperty("paths").TryGetProperty("/api/customers/{customerId}/overview", out _).ShouldBeTrue();
        schema.RootElement.GetProperty("paths").GetProperty("/api/customers").TryGetProperty("get", out _).ShouldBeTrue();
    }

    [Test] public async Task AcceptsActualUserJwtsInPublishedApplication()
    {
        var reader = Environment.GetEnvironmentVariable("TEST_READER_TOKEN");
        var writer = Environment.GetEnvironmentVariable("TEST_WRITER_TOKEN");
        if (string.IsNullOrWhiteSpace(reader) || string.IsNullOrWhiteSpace(writer))
            Assert.Ignore("The template verification script supplies isolated CLI-issued tokens.");
        _client.DefaultRequestHeaders.Authorization = new("Bearer", reader);
        (await _client.GetAsync("/api/customers/CUST-001/overview")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await _client.GetFromJsonAsync<JsonElement>("/api/customers");
        list.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("customerId").GetString())
            .ShouldBe(new[] { "CUST-001", "CUST-WARN" });
        _client.DefaultRequestHeaders.Authorization = new("Bearer", writer);
        var id = "CLI-" + Guid.NewGuid().ToString("N");
        using var body = new StringContent(JsonSerializer.Serialize(new { customerId = id, displayName = "CLI issued writer" }), System.Text.Encoding.UTF8, "application/json");
        (await _client.PostAsync("/api/customers", body)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.GetAsync("/api/customers")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _client.DefaultRequestHeaders.Authorization = new("Bearer", reader);
        var afterCreate = await _client.GetFromJsonAsync<JsonElement>("/api/customers");
        afterCreate.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("customerId").GetString()).ShouldContain(id);
        _client.DefaultRequestHeaders.Authorization = new("Bearer", writer);
        (await _client.DeleteAsync("/api/customers/" + id)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.GetAsync("/api/customers/CUST-001/overview")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
