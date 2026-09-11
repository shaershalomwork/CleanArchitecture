using System.IdentityModel.Tokens.Jwt;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CleanArchitecture.Application.FunctionalTests.Authentication;

public class LocalAuthenticationTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("local-fixture-key-not-a-real-developer-secret-12345");
    private sealed class Factory(bool key = true, bool external = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test").ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sources:CustomerRegistry:Mode"] = "Fake", ["Sources:Billing:Mode"] = "Fake",
                ["Authentication:Mode"] = external ? "External" : "Development",
                ["Authentication:Authority"] = "https://external.example", ["Authentication:Audience"] = "external-api",
                ["Authentication:ClientId"] = "fixture", ["Authentication:ClientSecret"] = "fixture",
                ["Authentication:Schemes:Bearer:ValidIssuer"] = "dotnet-user-jwts",
                ["Authentication:Schemes:Bearer:ValidAudiences:0"] = "local-api",
                ["Authentication:Schemes:Bearer:SigningKeys:0:Issuer"] = "dotnet-user-jwts",
                ["Authentication:Schemes:Bearer:SigningKeys:0:Value"] = key ? Convert.ToBase64String(Key) : null
            }));
            if (external) builder.ConfigureTestServices(services => services.PostConfigure<JwtBearerOptions>("Bearer", options =>
            {
                var metadata = new OpenIdConnectConfiguration { Issuer = "https://external.example" };
                metadata.SigningKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("external-fixture-key-unrelated-to-local-key-12345")));
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
            }));
        }
    }
    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
    private static string Token(string permission = "customers.write", string issuer = "dotnet-user-jwts", string audience = "local-api", int timing = 0, bool badKey = false)
    {
        var token = new JwtSecurityToken(issuer, audience,
            [new("sub", "cli-user"), new("unique_name", "CLI User"), new("role", "operator"), new("permissions", permission)],
            DateTime.UtcNow.AddMinutes(timing > 0 ? 20 : -30), DateTime.UtcNow.AddMinutes(timing < 0 ? -20 : 60),
            new SigningCredentials(new SymmetricSecurityKey(badKey ? Encoding.UTF8.GetBytes("wrong-fixture-key-unrelated-to-local-key-123456") : Key), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    private static async Task<HttpResponseMessage> Mutation(HttpClient client, string method)
    {
        var path = method == "LOGOUT" ? "/auth/logout" : method == "POST" ? "/api/customers" : "/api/customers/CUST-001";
        using var request = new HttpRequestMessage(new HttpMethod(method == "LOGOUT" ? "POST" : method), path);
        if (method is "POST" or "PUT" or "PATCH") request.Content = JsonContent.Create(method == "POST" ? (object)new { customerId = "AUTH-NEW", displayName = "Example" } : new { displayName = "Example" });
        return await client.SendAsync(request);
    }
    [TestCase("reader", true, false)]
    [TestCase("writer", false, true)]
    [TestCase("reader-writer", true, true)]
    [TestCase("no-access", false, false)]
    public async Task ProfilesExposePolicyEvaluatedCapabilities(string profile, bool read, bool write)
    {
        await using var factory = new Factory(); using var client = Client(factory);
        await CustomerFixtures.RegisterAsync(factory.Services, "CUST-001");
        (await client.GetAsync("/auth/login?profile=" + profile)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var me = await client.GetFromJsonAsync<JsonElement>("/auth/me");
        me.GetProperty("capabilities").GetProperty("canReadCustomers").GetBoolean().ShouldBe(read);
        me.GetProperty("capabilities").GetProperty("canWriteCustomers").GetBoolean().ShouldBe(write);
        (await client.GetAsync("/api/customers/CUST-001/overview")).StatusCode.ShouldBe(read ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
    }
    [TestCase("Bearer")]
    [TestCase("Bearer invalid")]
    [TestCase("bearer invalid")]
    [TestCase("Bearer\tinvalid")]
    public async Task BadBearerNeverFallsBackToCookie(string header)
    {
        await using var factory = new Factory(); using var client = Client(factory);
        await client.GetAsync("/auth/login?profile=reader-writer");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", header);
        var response = await Mutation(client, "POST");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Single().Scheme.ShouldBe("Bearer");
    }
    [TestCase("reader-writer", "customers.read", 403)]
    [TestCase("reader", "customers.write", 200)]
    public async Task MixedCredentialsUseOnlyBearerPermissions(string profile, string permission, int expected)
    {
        await using var factory = new Factory(); using var client = Client(factory);
        await client.GetAsync("/auth/login?profile=" + profile);
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(permission));
        ((int)(await Mutation(client, "POST")).StatusCode).ShouldBe(expected);
    }
    [TestCase("POST")]
    [TestCase("PUT")]
    [TestCase("PATCH")]
    [TestCase("DELETE")]
    [TestCase("LOGOUT")]
    public async Task LocalBearerMutationsDoNotRequireCsrf(string method)
    {
        await using var factory = new Factory(); using var client = Client(factory);
        await CustomerFixtures.RegisterAsync(factory.Services, "CUST-001");
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token());
        (await Mutation(client, method)).StatusCode.ShouldBe(method == "LOGOUT" ? HttpStatusCode.Redirect : HttpStatusCode.OK);
    }
    [TestCase("POST")]
    [TestCase("PUT")]
    [TestCase("PATCH")]
    [TestCase("DELETE")]
    [TestCase("LOGOUT")]
    public async Task RealCookieMutationsRequireMatchingCsrf(string method)
    {
        await using var factory = new Factory(); using var client = Client(factory);
        await CustomerFixtures.RegisterAsync(factory.Services, "CUST-001");
        await client.GetAsync("/auth/login?profile=reader-writer");
        (await Mutation(client, method)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "invalid");
        (await Mutation(client, method)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var csrf = await client.GetFromJsonAsync<JsonElement>("/auth/antiforgery");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        (await Mutation(client, method)).StatusCode.ShouldBe(method == "LOGOUT" ? HttpStatusCode.Redirect : HttpStatusCode.OK);
    }
    [TestCase("wrong", "local-api", 0, false)]
    [TestCase("dotnet-user-jwts", "wrong", 0, false)]
    [TestCase("dotnet-user-jwts", "local-api", -1, false)]
    [TestCase("dotnet-user-jwts", "local-api", 1, false)]
    [TestCase("dotnet-user-jwts", "local-api", 0, true)]
    public async Task LocalTokensValidateAllBoundaries(string issuer, string audience, int timing, bool badKey)
    {
        await using var factory = new Factory(); using var client = Client(factory);
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(issuer: issuer, audience: audience, timing: timing, badKey: badKey));
        (await client.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
    [Test] public async Task ExternalModeRejectsLocalKeysEvenWhenConfigured()
    {
        await using var factory = new Factory(external: true); using var client = Client(factory);
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token());
        (await client.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = null;
        var options = await client.GetFromJsonAsync<JsonElement>("/auth/options");
        options.GetProperty("developmentProfiles").GetArrayLength().ShouldBe(0);
        (await client.GetAsync("/auth/login?profile=reader-writer")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
    [Test] public async Task MissingKeyDoesNotBreakCookieLogin()
    {
        await using var factory = new Factory(key: false); using var client = Client(factory);
        (await client.GetAsync("/auth/login")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await client.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token());
        (await client.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
    [Test] public async Task CliClaimsAreNormalizedWithoutGrantingRolePermissions()
    {
        await using var factory = new Factory(); using var client = Client(factory);
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(permission: "unrelated"));
        var me = await client.GetFromJsonAsync<JsonElement>("/auth/me");
        me.GetProperty("name").GetString().ShouldBe("CLI User");
        me.GetProperty("roles")[0].GetString().ShouldBe("operator");
        me.GetProperty("capabilities").GetProperty("canReadCustomers").GetBoolean().ShouldBeFalse();
        me.GetProperty("capabilities").GetProperty("canWriteCustomers").GetBoolean().ShouldBeFalse();
    }
    [Test] public async Task IdentityChangeInvalidatesOldAntiforgeryToken()
    {
        await using var factory = new Factory(); using var client = Client(factory);
        await client.GetAsync("/auth/login?profile=reader");
        var csrf = await client.GetFromJsonAsync<JsonElement>("/auth/antiforgery");
        await client.GetAsync("/auth/login?profile=writer");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        (await Mutation(client, "POST")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
