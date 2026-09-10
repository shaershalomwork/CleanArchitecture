using System.Security.Claims;
using CleanArchitecture.Web.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Web.Endpoints;
public sealed class Authentication : IEndpointGroup
{
    public static string RoutePrefix => "/auth";
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(Login, "login").AllowAnonymous();
        group.MapPost(Logout, "logout").RequireAuthorization();
        group.MapGet(Me, "me").RequireAuthorization();
        group.MapGet(Antiforgery, "antiforgery").AllowAnonymous();
    }

    [EndpointSummary("Start sign-in")]
    [EndpointDescription("Starts the configured browser sign-in flow, or signs in the fixed demo reader in Development/Test. Only local return URLs are accepted. External API-only deployments return 404 because they use bearer tokens.")]
    public static IResult Login(HttpContext context, IOptions<CleanArchitecture.Web.Authentication.AuthenticationOptions> options, string? returnUrl = null)
    {
        var redirect = IsLocal(returnUrl) ? returnUrl! : "/";
        if (options.Value.Mode == "Development")
        {
            // This mode is rejected at startup outside Development/Test.
            var identity = new ClaimsIdentity([
                new Claim("sub", "demo-reader"), new Claim("name", "Demo reader"),
                new Claim("permissions", "customers.read")], CookieAuthenticationDefaults.AuthenticationScheme, "name", "roles");
            return Results.SignIn(new ClaimsPrincipal(identity), new AuthenticationProperties { RedirectUri = redirect },
                CookieAuthenticationDefaults.AuthenticationScheme);
        }
        return AuthenticationRegistration.HasSpa
            ? Results.Challenge(new AuthenticationProperties { RedirectUri = redirect }, [OpenIdConnectDefaults.AuthenticationScheme])
            : Results.NotFound();
    }
    [EndpointSummary("Sign out of the browser session")]
    [EndpointDescription("Requires authentication and a valid antiforgery token for cookie sessions. Clears the local session, performs external sign-out when configured, and redirects to the application root.")]
    public static IResult Logout(IOptions<CleanArchitecture.Web.Authentication.AuthenticationOptions> options) =>
        Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
            options.Value.Mode == "External" && AuthenticationRegistration.HasSpa
                ? [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]
                : [CookieAuthenticationDefaults.AuthenticationScheme]);

    [EndpointSummary("Get the signed-in user's name")]
    [EndpointDescription("Requires authentication. Returns the current identity's display name, which can be null if the identity provider supplied no name claim.")]
    public static IResult Me(HttpContext context) => Results.Ok(new { name = context.User.Identity?.Name });
    [EndpointSummary("Get an antiforgery request token")]
    [EndpointDescription("Returns a token and sets its paired antiforgery cookie. Retain the cookie and send the token in X-CSRF-TOKEN for cookie-authenticated POST, PUT, PATCH, and DELETE requests.")]
    public static IResult Antiforgery(HttpContext context, IAntiforgery antiforgery) =>
        Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken });

    private static bool IsLocal(string? url) => url is { Length: > 0 } && url[0] == '/' &&
        (url.Length == 1 || (url[1] != '/' && url[1] != '\\')) && !url.Any(char.IsControl);
}
