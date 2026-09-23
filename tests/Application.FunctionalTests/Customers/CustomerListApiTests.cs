using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CleanArchitecture.Application.FunctionalTests.Customers;

public abstract class CustomerListApiContractTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    protected virtual WebApplicationFactory<Program> CreateFactory() => new WebApiFactory();

    [SetUp] public async Task Setup()
    {
        _factory = CreateFactory();
        _client = _factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        _client.DefaultRequestHeaders.Add("X-Test-User", "reader-writer");
        var csrf = await _client.GetFromJsonAsync<JsonElement>("/auth/antiforgery");
        _client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    [TearDown] public async Task Cleanup()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<JsonElement[]> List()
    {
        using var response = await _client.GetAsync("/api/customers");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("status").GetString().ShouldBe("Success");
        json.GetProperty("issues").GetArrayLength().ShouldBe(0);
        response.Headers.GetValues("X-Correlation-ID").Single().ShouldBe(json.GetProperty("correlationId").GetString());
        return json.GetProperty("data").EnumerateArray().ToArray();
    }

    [Test] public async Task OnlyExplicitRegistrationMakesCustomersVisible()
    {
        (await List()).ShouldBeEmpty();
        for (var i = 0; i < 2; i++)
            (await _client.GetAsync("/api/customers/NEW-ID/overview")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await List()).ShouldBeEmpty();
        (await _client.PostAsJsonAsync("/api/customers", new { customerId = "NEW-ID", displayName = "Registered name" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.PostAsJsonAsync("/api/customers", new { customerId = "A-ID", displayName = "First" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await List();
        rows.Select(x => x.GetProperty("customerId").GetString()).ShouldBe(new[] { "A-ID", "NEW-ID" });
        rows[1].GetProperty("displayName").GetString().ShouldBe("Registered name");
        rows[1].GetProperty("observedAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
        (await _client.PutAsJsonAsync("/api/customers/NEW-ID", new { displayName = "Replacement" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await List())[1].GetProperty("displayName").GetString().ShouldBe("Replacement");
        (await _client.PatchAsJsonAsync("/api/customers/NEW-ID", new { displayName = "Renamed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var overview = await _client.GetFromJsonAsync<JsonElement>("/api/customers/NEW-ID/overview");
        overview.GetProperty("data").GetProperty("displayName").GetString().ShouldBe("Renamed");
        (await List())[1].GetProperty("displayName").GetString().ShouldBe("Renamed");
        (await _client.DeleteAsync("/api/customers/NEW-ID")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.GetAsync("/api/customers/NEW-ID/overview")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await List()).Length.ShouldBe(1);
        (await _client.PostAsJsonAsync("/api/customers", new { customerId = "NEW-ID", displayName = "Recreated" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await List())[1].GetProperty("displayName").GetString().ShouldBe("Recreated");
    }

    [Test] public async Task ListingDoesNotDependOnBillingAvailability()
    {
        await CustomerFixtures.RegisterAsync(_factory.Services, "CUST-WARN");
        (await List()).Single().GetProperty("customerId").GetString().ShouldBe("CUST-WARN");
        var overview = await _client.GetFromJsonAsync<JsonElement>("/api/customers/CUST-WARN/overview");
        overview.GetProperty("status").GetString().ShouldBe("Warning");
    }

    [TestCase(null, 401)]
    [TestCase("writer", 403)]
    [TestCase("without-permission", 403)]
    [TestCase("reader", 200)]
    [TestCase("reader-writer", 200)]
    public async Task RequiresReadPermission(string? user, int status)
    {
        _client.DefaultRequestHeaders.Remove("X-Test-User");
        _client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        if (user is not null) _client.DefaultRequestHeaders.Add("X-Test-User", user);
        using var response = await _client.GetAsync("/api/customers");
        ((int)response.StatusCode).ShouldBe(status);
        if (status != 200)
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            json.GetProperty("data").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }
}

public sealed class CustomerListApiTests : CustomerListApiContractTests;
