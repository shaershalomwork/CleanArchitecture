using System.Diagnostics;
using System.Reflection;
using CleanArchitecture.Web.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
// The build-time OpenAPI host must not resolve corporate configuration or credentials.
if (Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider")
{
    builder.Environment.EnvironmentName = "Test";
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Sources:CustomerRegistry:Mode"] = "Fake", ["Sources:Billing:Mode"] = "Fake",
        ["Authentication:Mode"] = "Development", ["AZURE_KEY_VAULT_ENDPOINT"] = ""
    });
}
builder.AddServiceDefaults();
builder.AddKeyVaultIfConfigured();
builder.AddApplicationServices();
builder.AddInfrastructureServices();
builder.AddWebServices();

var app = builder.Build();
app.Use(async (context, next) =>
{
    context.TraceIdentifier = Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString();
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    await next(context);
});
app.UseExceptionHandler();
app.UseStatusCodePages(context => OperationResultMapper.WriteErrorAsync(context.HttpContext,
    context.HttpContext.Response.StatusCode, "HTTP." + context.HttpContext.Response.StatusCode, "The request could not be completed."));
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseFileServer();
app.UseRouting();
app.UseCors();
app.UseRequestTimeouts();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if ((context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<CleanArchitecture.Web.Authentication.AuthenticationOptions>>().Value.Mode == "Development" || AuthenticationRegistration.HasSpa) &&
        context.User.Identity?.IsAuthenticated == true &&
        context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException)
        {
            await OperationResultMapper.WriteErrorAsync(context, 400, "REQUEST.CSRF", "The antiforgery token is invalid.");
            return;
        }
    }
    await next(context);
});
app.MapOpenApi().AllowAnonymous();
app.MapScalarApiReference().AllowAnonymous();
app.MapDefaultEndpoints();
app.MapEndpoints(typeof(Program).Assembly);
#if (UseAngular)
app.MapFallbackToFile("index.html").AllowAnonymous();
#else
app.MapGet("/", () => Results.Redirect("/scalar")).AllowAnonymous();
#endif
app.Run();
public partial class Program;
