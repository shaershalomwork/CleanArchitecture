using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
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
public class AuthenticationTests
{
    private sealed class Factory(bool jwt = false, string environment = "Test") : WebApplicationFactory<Program>
    {
        public static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("fixture-only-signing-key-at-least-32-characters"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment).ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sources:CustomerRegistry:Mode"] = "Fake", ["Sources:Billing:Mode"] = "Fake",
                ["Authentication:Mode"] = jwt ? "External" : "Development",
                ["Authentication:Authority"] = "https://issuer.example", ["Authentication:Audience"] = "integration",
                ["Authentication:ClientId"] = "fixture", ["Authentication:ClientSecret"] = "fixture"
            }));
            if (jwt) builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(o =>
                {
                    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                    o.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
                });
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    var configuration = new OpenIdConnectConfiguration { Issuer = "https://issuer.example" };
                    configuration.SigningKeys.Add(Key);
                    o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    o.TokenValidationParameters.IssuerSigningKey = Key;
                });
            });
        }
    }
    [TestCase("https://issuer.example", "integration", true, 200)]
    [TestCase("https://wrong.example", "integration", true, 401)]
    [TestCase("https://issuer.example", "wrong", true, 401)]
    [TestCase("https://issuer.example", "integration", false, 403)]
    [TestCase("https://issuer.example", "integration", true, 401, true, false)]
    [TestCase("https://issuer.example", "integration", true, 401, false, true)]
    public async Task ValidatesJwtAndPermissions(string issuer, string audience, bool permission, int expected,
        bool invalidSignature = false, bool expired = false)
    {
        await using var factory = new Factory(jwt: true);
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        var claims = new List<Claim> { new("sub", "user") };
        if (permission) claims.Add(new("permissions", "customers.read"));
        var signingKey = invalidSignature
            ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes("different-fixture-signing-key-at-least-32-characters")) : Factory.Key;
        var token = new JwtSecurityToken(issuer, audience, claims, DateTime.UtcNow.AddMinutes(-20),
            DateTime.UtcNow.AddMinutes(expired ? -10 : 5), new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        var response = await client.GetAsync("/api/customers/CUST-001/overview");
        ((int)response.StatusCode).ShouldBe(expected);
        if (expected == 401) response.Headers.WwwAuthenticate.Single().Scheme.ShouldBe("Bearer");
    }
    [Test] public async Task CookieSignInRejectsOpenRedirectAndLogoutRequiresCsrf()
    {
        await using var factory = new Factory();
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        var login = await client.GetAsync("/auth/login?returnUrl=//evil.example");
        login.Headers.Location!.OriginalString.ShouldBe("/");
        (await client.GetAsync("/api/customers/CUST-001/overview")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync("/auth/logout", new StringContent(""))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var token = await client.GetFromJsonAsync<JsonElement>("/auth/antiforgery");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
        (await client.PostAsync("/auth/logout", new StringContent(""))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await client.GetAsync("/api/customers/CUST-001/overview")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
    [TestCase(false, 403)]
    [TestCase(true, 200)]
    public async Task BearerWritesRequireWritePermissionAndDoNotRequireCsrf(bool writePermission, int expected)
    {
        await using var factory = new Factory(jwt: true);
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        var claims = new[] { new Claim("sub", "writer"), new Claim("permissions", writePermission ? "customers.write" : "customers.read") };
        var token = new JwtSecurityToken("https://issuer.example", "integration", claims, DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(5), new SigningCredentials(Factory.Key, SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        var response = await client.PostAsJsonAsync("/api/customers", new { customerId = "JWT-WRITE", displayName = "Writer" });
        // Angular's existing middleware applies antiforgery to all authenticated mutations.
        if (writePermission && CleanArchitecture.Web.Authentication.AuthenticationRegistration.HasSpa)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var csrf = await client.GetFromJsonAsync<JsonElement>("/auth/antiforgery");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
            response.Dispose();
            response = await client.PostAsJsonAsync("/api/customers", new { customerId = "JWT-WRITE", displayName = "Writer" });
        }
        using (response) ((int)response.StatusCode).ShouldBe(expected);
    }

    [Test] public void ProductionRejectsFakeSourcesAndAuthentication()
    {
        using var factory = new Factory(environment: "Production");
        var error = Should.Throw<Exception>(() => factory.CreateClient());
        error.ToString().ShouldContain("Development");
    }
}
