using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Web.Authentication;

// Remote callback handlers are active only for the external Angular login flow.
public sealed class IntegrationSchemeProvider(
    IOptions<Microsoft.AspNetCore.Authentication.AuthenticationOptions> schemes,
    IOptions<AuthenticationOptions> settings) : AuthenticationSchemeProvider(schemes)
{
    public override async Task<IEnumerable<AuthenticationScheme>> GetRequestHandlerSchemesAsync()
    {
        var handlers = await base.GetRequestHandlerSchemesAsync();
        return AuthenticationRegistration.HasSpa && settings.Value.Mode == "External"
            ? handlers
            : handlers.Where(x => x.Name != OpenIdConnectDefaults.AuthenticationScheme);
    }
}
