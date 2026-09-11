using System.Security.Claims;
using CleanArchitecture.Web.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using CleanArchitecture.Web.Contracts;

namespace CleanArchitecture.Web.Endpoints;
public sealed class Authentication : IEndpointGroup
{
    public static string RoutePrefix => "/auth";
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(Login, "login").AllowAnonymous();
        group.MapPost(Logout, "logout").RequireAuthorization();
        group.MapGet(Me, "me").RequireAuthorization().Produces<CurrentSessionResponse>();
        group.MapGet(Antiforgery, "antiforgery").AllowAnonymous().Produces<AntiforgeryResponse>();
        group.MapGet(GetAuthenticationOptions, "options").AllowAnonymous().Produces<AuthenticationUiOptionsResponse>();
    }

    [EndpointSummary("Start sign-in")]
    [EndpointDescription("Starts browser sign-in. Development/Test accepts a fixed reader, writer, reader-writer, or no-access profile; the default is reader. Only local return URLs are accepted. External API-only deployments return 404 because they use bearer tokens.")]
    public static IResult Login(HttpContext context, IOptions<CleanArchitecture.Web.Authentication.AuthenticationOptions> options, string? returnUrl = null, string? profile = null)
    {
        var redirect = IsLocal(returnUrl) ? returnUrl! : "/";
        if (options.Value.Mode == "Development")
        {
            // This mode is rejected at startup outside Development/Test.
            profile ??= "reader";
            var selected = Profiles.SingleOrDefault(p => p.Id == profile);
            if (selected is null) return Results.BadRequest();
            var claims = new List<Claim> { new("sub", "demo-" + profile), new(options.Value.NameClaimType, "Demo " + selected.Label) };
            if (profile is "reader" or "reader-writer") claims.Add(new("permissions", "customers.read"));
            if (profile is "writer" or "reader-writer") claims.Add(new("permissions", "customers.write"));
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, options.Value.NameClaimType, options.Value.RoleClaimType);
            return Results.SignIn(new ClaimsPrincipal(identity), new AuthenticationProperties { RedirectUri = redirect },
                CookieAuthenticationDefaults.AuthenticationScheme);
        }
        if (profile is not null) return Results.BadRequest();
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

    [EndpointSummary("Get the signed-in user's identity and access")]
    [EndpointDescription("Returns the authenticated subject, nullable display name, supplied roles and permissions, and customer capabilities evaluated using backend policies. Roles do not implicitly grant permissions. Returns 401 when not authenticated.")]
    public static async Task<IResult> Me(HttpContext context, IAuthorizationService authorization,
        IOptions<CleanArchitecture.Web.Authentication.AuthenticationOptions> options)
    {
        context.Response.Headers.CacheControl = "no-store";
        var read = await authorization.AuthorizeAsync(context.User, "CustomerOverview.Read");
        var write = await authorization.AuthorizeAsync(context.User, "Customer.Write");
        return Results.Ok(new CurrentSessionResponse(context.User.FindFirst("sub")?.Value, context.User.Identity?.Name,
            context.User.FindAll(options.Value.RoleClaimType).Select(c => c.Value).Distinct().ToArray(),
            context.User.FindAll("permissions").Select(c => c.Value).Distinct().ToArray(), new(read.Succeeded, write.Succeeded)));
    }
    private static readonly DevelopmentProfileResponse[] Profiles =
        [new("reader", "Reader"), new("writer", "Writer"), new("reader-writer", "Reader/writer"), new("no-access", "No access")];

    [EndpointSummary("Get available sign-in methods")]
    [EndpointDescription("Returns browser login availability and fixed local demo profiles in Development/Test mode. No credentials or provider configuration are returned.")]
    public static IResult GetAuthenticationOptions(HttpContext context, IOptions<CleanArchitecture.Web.Authentication.AuthenticationOptions> options)
    {
        context.Response.Headers.CacheControl = "no-store";
        var development = options.Value.Mode == "Development";
        return Results.Ok(new AuthenticationUiOptionsResponse(development || AuthenticationRegistration.HasSpa, development ? Profiles : []));
    }
    [EndpointSummary("Get an antiforgery request token")]
    [EndpointDescription("Returns a token and sets its paired antiforgery cookie. Retain the cookie and send the token in X-CSRF-TOKEN for cookie-authenticated POST, PUT, PATCH, and DELETE requests.")]
    public static IResult Antiforgery(HttpContext context, IAntiforgery antiforgery) =>
        Results.Ok(new AntiforgeryResponse(antiforgery.GetAndStoreTokens(context).RequestToken));

    private static bool IsLocal(string? url) => url is { Length: > 0 } && url[0] == '/' &&
        (url.Length == 1 || (url[1] != '/' && url[1] != '\\')) && !url.Any(char.IsControl);
}
