using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Connections;
using CleanArchitecture.Infrastructure.Execution;
using CleanArchitecture.Infrastructure.Fakes;
using CleanArchitecture.Infrastructure.Observability;
using CleanArchitecture.Infrastructure.Sources.Billing;
using CleanArchitecture.Infrastructure.Sources.CustomerRegistry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;
public static class DependencyInjection
{
    public static void AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
        var local = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test");
        builder.Services.AddOptions<CustomerRegistryOptions>().Bind(builder.Configuration.GetSection("Sources:CustomerRegistry"))
            .Validate(o => Enum.IsDefined(o.Provider), "Unknown customer database provider.")
            .Validate(o => Enum.IsDefined(o.Mode) && (local || o.Mode == SourceMode.Live), "Fake sources are restricted to Development and Test.")
            .Validate(o => o.Mode == SourceMode.Fake || !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(o.ConnectionName)), "CustomerRegistry requires its named connection string.")
            .ValidateOnStart();
        builder.Services.AddOptions<BillingOptions>().Bind(builder.Configuration.GetSection("Sources:Billing"))
            .Validate(o => Enum.IsDefined(o.Mode) && (local || o.Mode == SourceMode.Live), "Fake sources are restricted to Development and Test.")
            .Validate(o => o.Mode == SourceMode.Fake || (Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "https" || (local && uri.Scheme == "http")) && o.BaseUrl.EndsWith('/') &&
                !string.IsNullOrWhiteSpace(o.ApiKey)), "Billing requires an HTTPS base URL ending in / and an API key.")
            .ValidateOnStart();
        builder.Services.AddOptions<SourceExecutionOptions>().Bind(builder.Configuration.GetSection("SourceExecution"))
            .ValidateDataAnnotations().Validate(o => o.TotalTimeoutSeconds >= o.AttemptTimeoutSeconds && o.SamplingSeconds >= 2 * o.AttemptTimeoutSeconds, "Invalid execution budgets.")
            .ValidateOnStart();
        builder.Services.AddHealthChecks().AddCheck<CustomerRegistryHealthCheck>("CustomerRegistry").AddCheck<BillingHealthCheck>("Billing");
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<ICorrelationContext, CorrelationContext>();
        builder.Services.AddSingleton<SourcePipelines>();
        builder.Services.AddScoped<SourceExecutor>();
        builder.Services.AddScoped<SqlConnectionFactory>();
        builder.Services.AddScoped<SqliteConnectionFactory>();
        builder.Services.AddScoped<SqliteCustomerSourceAdapter>();
        builder.Services.AddScoped<FakeCustomerSourceAdapter>();
        builder.Services.AddScoped<SqlCustomerSourceAdapter>();
        builder.Services.AddScoped<FakeBillingSourceAdapter>();
        builder.Services.AddHttpClient("Billing", (sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<BillingOptions>>().Value.BaseUrl);
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.MaxResponseContentBufferSize = 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddScoped<HttpBillingSourceAdapter>(sp => ActivatorUtilities.CreateInstance<HttpBillingSourceAdapter>(
            sp, sp.GetRequiredService<IHttpClientFactory>().CreateClient("Billing")));
        builder.Services.AddScoped<ICustomerSourceAdapter>(sp => sp.GetRequiredService<IOptions<CustomerRegistryOptions>>().Value.Mode == SourceMode.Fake
            ? sp.GetRequiredService<FakeCustomerSourceAdapter>()
            : sp.GetRequiredService<IOptions<CustomerRegistryOptions>>().Value.Provider == CustomerDatabaseProvider.SQLite
                ? sp.GetRequiredService<SqliteCustomerSourceAdapter>() : sp.GetRequiredService<SqlCustomerSourceAdapter>());
        builder.Services.AddScoped<IBillingSourceAdapter>(sp => sp.GetRequiredService<IOptions<BillingOptions>>().Value.Mode == SourceMode.Fake
            ? sp.GetRequiredService<FakeBillingSourceAdapter>() : sp.GetRequiredService<HttpBillingSourceAdapter>());
    }
}
