using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using CleanArchitecture.Web.Infrastructure;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.IdentityModel.Tokens.Jwt;

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
        var local = (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test")) &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST"));
        builder.Services.AddOptions<AuthenticationOptions>().Bind(builder.Configuration.GetSection("Authentication"))
            .Validate(o => o.Mode is "External" or "Development", "Unknown authentication mode.")
            .Validate(o => o.Mode != "Development" || local, "Development authentication cannot run outside Development or Test.")
            .Validate(o => o.Mode == "Development" || (Uri.TryCreate(o.Authority, UriKind.Absolute, out var uri) &&
                uri.Scheme == "https" && !string.IsNullOrWhiteSpace(o.Audience)), "External authentication requires HTTPS authority and audience.")
            .Validate(o => o.Mode == "Development" || !HasSpa || (!string.IsNullOrWhiteSpace(o.ClientId) && !string.IsNullOrWhiteSpace(o.ClientSecret)),
                "SPA authentication requires an OIDC client ID and secret.")
            .Validate(o => o.MaximumAgeSeconds is > 0 and <= 300, "Maximum authentication age must be at most five minutes.")
            .Validate(o => o.Mode != "External" || !HasSpa ||
                (Uri.TryCreate(o.PublicOrigin, UriKind.Absolute, out var origin) && origin.Scheme == "https" &&
                 origin.AbsolutePath == "/" && string.IsNullOrEmpty(origin.Query) && string.IsNullOrEmpty(origin.Fragment)),
                "Browser authentication requires a canonical HTTPS public origin.")
            .Validate(o => o.Mode != "External" || o.DataProtectionKeysPath is null ||
                (!string.IsNullOrWhiteSpace(o.DataProtectionCertificatePath) && !string.IsNullOrWhiteSpace(o.DataProtectionApplicationName)),
                "Persistent authentication keys require a certificate and application name.")
            .ValidateOnStart();

        var settings = builder.Configuration.GetSection("Authentication").Get<AuthenticationOptions>() ?? new();
        if (settings.DataProtectionKeysPath is { Length: > 0 } keyPath)
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(settings.DataProtectionCertificatePath!, null);
            builder.Services.AddDataProtection().SetApplicationName(settings.DataProtectionApplicationName!)
                .PersistKeysToFileSystem(new DirectoryInfo(keyPath)).ProtectKeysWithCertificate(certificate);
        }
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = 1;
            foreach (var proxy in settings.TrustedProxies)
            {
                if (!IPAddress.TryParse(proxy, out var address)) throw new InvalidOperationException("Invalid trusted proxy address.");
                options.KnownProxies.Add(address);
            }
        });

        var auth = builder.Services.AddAuthentication("Integration")
            .AddPolicyScheme("Integration", "Integration", options =>
                options.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.Any(value => value is not null &&
                        value.TrimStart().Split([' ', '\t', ','], 2)[0].Equals("Bearer", StringComparison.OrdinalIgnoreCase))
                        ? JwtBearerDefaults.AuthenticationScheme
                        : context.RequestServices.GetRequiredService<IOptions<AuthenticationOptions>>().Value.Mode == "Development" || HasSpa
                        ? CookieAuthenticationDefaults.AuthenticationScheme : JwtBearerDefaults.AuthenticationScheme);
        auth.AddCookie(options =>
        {
            options.Cookie.Name = "__Host-Integration";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = false;
            options.ExpireTimeSpan = TimeSpan.FromSeconds(settings.MaximumAgeSeconds);
            options.Events.OnValidatePrincipal = context =>
            {
                var current = context.HttpContext.RequestServices.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
                if (current.Mode != "External") return Task.CompletedTask;
                var issued = context.Principal?.FindFirst("auth_token_iat")?.Value;
                var expires = context.Principal?.FindFirst("auth_token_exp")?.Value;
                if (!long.TryParse(issued, out var iat) || !long.TryParse(expires, out var exp) ||
                    DateTimeOffset.UtcNow >= DateTimeOffset.FromUnixTimeSeconds(Math.Min(exp, iat + current.MaximumAgeSeconds)))
                    context.RejectPrincipal();
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToLogin = c => OperationResultMapper.WriteErrorAsync(c.HttpContext, 401, "AUTH.UNAUTHENTICATED", "Authentication is required.");
            options.Events.OnRedirectToAccessDenied = c => OperationResultMapper.WriteErrorAsync(c.HttpContext, 403, "AUTH.FORBIDDEN", "Access is denied.");
        });
        auth.AddJwtBearer(options => options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var settings = context.HttpContext.RequestServices.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
                if (settings.Mode == "External")
                {
                    var issued = context.Principal?.FindFirst(JwtRegisteredClaimNames.Iat)?.Value;
                    if (!long.TryParse(issued, out var iat) ||
                        DateTimeOffset.UtcNow >= DateTimeOffset.FromUnixTimeSeconds(iat + settings.MaximumAgeSeconds) ||
                        DateTimeOffset.FromUnixTimeSeconds(iat) > DateTimeOffset.UtcNow)
                        context.Fail("Access token age is invalid.");
                }
                if (settings.Mode == "Development" && context.Principal?.Identity is ClaimsIdentity identity)
                {
                    if (!identity.HasClaim(c => c.Type == settings.NameClaimType) && identity.FindFirst("unique_name") is { } name)
                        identity.AddClaim(new Claim(settings.NameClaimType, name.Value));
                    foreach (var role in identity.FindAll("role").ToArray())
                        if (!identity.HasClaim(settings.RoleClaimType, role.Value)) identity.AddClaim(new Claim(settings.RoleClaimType, role.Value));
                }
                return Task.CompletedTask;
            },
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
                if (settings.Mode == "External")
                {
                    // Do not inherit user-jwts issuers, audiences or symmetric keys from
                    // Authentication:Schemes:Bearer when switching to External mode.
                    options.TokenValidationParameters = new TokenValidationParameters();
                    options.Authority = settings.Authority;
                    options.Audience = settings.Audience;
                    options.RequireHttpsMetadata = true;
                    options.BackchannelHttpHandler = CreateBackchannel(settings.CaBundlePath);
                }
                else
                {
                    options.Authority = null;
                    options.MetadataAddress = "";
                }
                options.MapInboundClaims = false;
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidateLifetime = true;
                options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
                options.TokenValidationParameters.ValidateIssuerSigningKey = true;
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
                options.RequireHttpsMetadata = true;
                options.BackchannelHttpHandler = CreateBackchannel(settings.CaBundlePath);
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Events.OnRedirectToIdentityProvider = context =>
                {
                    context.ProtocolMessage.RedirectUri = settings.PublicOrigin.TrimEnd('/') + options.CallbackPath;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToIdentityProviderForSignOut = context =>
                {
                    context.ProtocolMessage.PostLogoutRedirectUri = settings.PublicOrigin.TrimEnd('/') + options.SignedOutCallbackPath;
                    return Task.CompletedTask;
                };
                options.Events.OnTokenValidated = context =>
                {
                    if (context.Principal?.Identity is not ClaimsIdentity identity || context.SecurityToken is null)
                    {
                        context.Fail("ID token is unavailable.");
                        return Task.CompletedTask;
                    }
                    if (!long.TryParse(context.Principal.FindFirst("iat")?.Value, out var issuedAt))
                    {
                        context.Fail("ID token issuance is unavailable.");
                        return Task.CompletedTask;
                    }
                    identity.AddClaim(new Claim("auth_token_iat", issuedAt.ToString()));
                    identity.AddClaim(new Claim("auth_token_exp", new DateTimeOffset(context.SecurityToken.ValidTo).ToUnixTimeSeconds().ToString()));
                    // The ID token is retained only for RP-initiated logout inside the protected ticket.
                    if (context.TokenEndpointResponse?.IdToken is { Length: > 0 } idToken)
                        context.Properties!.StoreTokens([new Microsoft.AspNetCore.Authentication.AuthenticationToken { Name = "id_token", Value = idToken }]);
                    return Task.CompletedTask;
                };
                options.Events.OnTicketReceived = context =>
                {
                    var iat = long.Parse(context.Principal!.FindFirst("auth_token_iat")!.Value);
                    var exp = long.Parse(context.Principal.FindFirst("auth_token_exp")!.Value);
                    context.Properties!.ExpiresUtc = DateTimeOffset.FromUnixTimeSeconds(Math.Min(exp, iat + settings.MaximumAgeSeconds));
                    context.Properties.AllowRefresh = false;
                    return Task.CompletedTask;
                };
                options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
                options.Events.OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect("/sign-in?error=authentication_failed");
                    return Task.CompletedTask;
                };
                options.TokenValidationParameters.NameClaimType = settings.NameClaimType;
                options.TokenValidationParameters.RoleClaimType = settings.RoleClaimType;
            });
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy("CustomerOverview.Read", policy => policy.RequireAuthenticatedUser().RequireClaim("permissions", "customers.read"))
            .AddPolicy("Customer.Write", policy => policy.RequireAuthenticatedUser().RequireClaim("permissions", "customers.write"));
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
    }

    private static HttpMessageHandler CreateBackchannel(string? caBundlePath)
    {
        var handler = new HttpClientHandler();
        if (string.IsNullOrWhiteSpace(caBundlePath)) return handler;
        var trusted = new X509Certificate2Collection();
        trusted.ImportFromPemFile(caBundlePath);
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
        {
            if (certificate is null || (errors & System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch) != 0) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(trusted);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            return chain.Build(certificate);
        };
        return handler;
    }
}
