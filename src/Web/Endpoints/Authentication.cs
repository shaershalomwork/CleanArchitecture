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
    public static IResult Logout(IOptions<CleanArchitecture.Web.Authentication.AuthenticationOptions> options) =>
        Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
            options.Value.Mode == "External" && AuthenticationRegistration.HasSpa
                ? [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]
                : [CookieAuthenticationDefaults.AuthenticationScheme]);

    public static IResult Me(HttpContext context) => Results.Ok(new { name = context.User.Identity?.Name });
    public static IResult Antiforgery(HttpContext context, IAntiforgery antiforgery) =>
        Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken });

    private static bool IsLocal(string? url) => url is { Length: > 0 } && url[0] == '/' &&
        (url.Length == 1 || (url[1] != '/' && url[1] != '\\')) && !url.Any(char.IsControl);
}
