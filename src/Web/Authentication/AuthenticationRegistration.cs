using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using CleanArchitecture.Web.Infrastructure;

namespace CleanArchitecture.Web.Authentication;
public static class AuthenticationRegistration
{
    public static bool HasSpa =>
#if (UseAngular)
        true;
#else
        false;
#endif
    public static void AddIntegrationAuthentication(this WebApplicationBuilder builder)
    {
        var local = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test");
        builder.Services.AddOptions<AuthenticationOptions>().Bind(builder.Configuration.GetSection("Authentication"))
            .Validate(o => o.Mode is "External" or "Development", "Unknown authentication mode.")
            .Validate(o => o.Mode != "Development" || local, "Development authentication cannot run outside Development or Test.")
            .Validate(o => o.Mode == "Development" || (Uri.TryCreate(o.Authority, UriKind.Absolute, out var uri) &&
                uri.Scheme == "https" && !string.IsNullOrWhiteSpace(o.Audience)), "External authentication requires HTTPS authority and audience.")
            .Validate(o => o.Mode == "Development" || !HasSpa || (!string.IsNullOrWhiteSpace(o.ClientId) && !string.IsNullOrWhiteSpace(o.ClientSecret)),
                "SPA authentication requires an OIDC client ID and secret.")
            .ValidateOnStart();

        var auth = builder.Services.AddAuthentication("Integration")
            .AddPolicyScheme("Integration", "Integration", options =>
                options.ForwardDefaultSelector = context =>
                    context.RequestServices.GetRequiredService<IOptions<AuthenticationOptions>>().Value.Mode == "Development" || HasSpa
                        ? CookieAuthenticationDefaults.AuthenticationScheme : JwtBearerDefaults.AuthenticationScheme);
        auth.AddCookie(options =>
        {
            options.Cookie.Name = "__Host-Integration";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Events.OnRedirectToLogin = c => OperationResultMapper.WriteErrorAsync(c.HttpContext, 401, "AUTH.UNAUTHENTICATED", "Authentication is required.");
            options.Events.OnRedirectToAccessDenied = c => OperationResultMapper.WriteErrorAsync(c.HttpContext, 403, "AUTH.FORBIDDEN", "Access is denied.");
        });
        auth.AddJwtBearer(options => options.Events = new JwtBearerEvents
        {
            OnChallenge = c =>
            {
                c.HandleResponse();
                c.Response.Headers.WWWAuthenticate = "Bearer";
                return OperationResultMapper.WriteErrorAsync(c.HttpContext, 401, "AUTH.UNAUTHENTICATED", "Authentication is required.");
            },
            OnForbidden = c => OperationResultMapper.WriteErrorAsync(c.HttpContext, 403, "AUTH.FORBIDDEN", "Access is denied.")
        });
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthenticationOptions>>((options, configuration) =>
            {
                var settings = configuration.Value;
                options.Authority = settings.Authority;
                options.Audience = settings.Audience;
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = settings.NameClaimType;
                options.TokenValidationParameters.RoleClaimType = settings.RoleClaimType;
            });
        auth.AddOpenIdConnect();
        builder.Services.AddSingleton<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider, IntegrationSchemeProvider>();
        builder.Services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthenticationOptions>>((options, configuration) =>
            {
                var settings = configuration.Value;
                options.Authority = settings.Authority;
                options.ClientId = settings.ClientId;
                options.ClientSecret = settings.ClientSecret;
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.ResponseType = "code";
                options.UsePkce = true;
                options.MapInboundClaims = false;
                options.SaveTokens = false;
                options.TokenValidationParameters.NameClaimType = settings.NameClaimType;
                options.TokenValidationParameters.RoleClaimType = settings.RoleClaimType;
            });
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy("CustomerOverview.Read", policy => policy.RequireAuthenticatedUser().RequireClaim("permissions", "customers.read"))
            .AddPolicy("Customer.Write", policy => policy.RequireAuthenticatedUser().RequireClaim("permissions", "customers.write"));
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
    }
}
