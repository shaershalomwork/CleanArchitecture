using Azure.Identity;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Web.Authentication;
using CleanArchitecture.Web.Services;
using Microsoft.AspNetCore.Http.Timeouts;

namespace Microsoft.Extensions.DependencyInjection;
public static class DependencyInjection
{
    public static void AddWebServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IUser, CurrentUser>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddOpenApi(options =>
        {
            options.AddOperationTransformer<ApiExceptionOperationTransformer>();
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        });
        builder.AddIntegrationAuthentication();
        builder.Services.AddRequestTimeouts(options => options.DefaultPolicy = new RequestTimeoutPolicy
        {
            Timeout = TimeSpan.FromSeconds(15), TimeoutStatusCode = 504,
            WriteTimeoutResponse = context => OperationResultMapper.WriteErrorAsync(context, 504, "REQUEST.TIMEOUT", "The request exceeded its time budget.")
        });
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        {
            if (origins.Length > 0) p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }));
    }
    public static void AddKeyVaultIfConfigured(this IHostApplicationBuilder builder)
    {
        var uri = builder.Configuration["AZURE_KEY_VAULT_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(uri))
            builder.Configuration.AddAzureKeyVault(new Uri(uri), new DefaultAzureCredential());
    }
}
