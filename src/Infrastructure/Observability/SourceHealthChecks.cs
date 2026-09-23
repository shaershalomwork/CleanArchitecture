using CleanArchitecture.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Observability;

public sealed class CustomerRegistryHealthCheck(IServiceScopeFactory scopes, IOptions<CustomerRegistryOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (options.Value.Mode == SourceMode.Fake) return HealthCheckResult.Healthy();
        using var scope = scopes.CreateScope();
        var probe = scope.ServiceProvider.GetRequiredKeyedService<IHealthCheck>(options.Value.Provider);
        return await probe.CheckHealthAsync(context, cancellationToken);
    }
}
public sealed class BillingHealthCheck(IHttpClientFactory clients, IOptions<BillingOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (options.Value.Mode == SourceMode.Fake) return HealthCheckResult.Healthy();
        using var client = clients.CreateClient("Billing");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "health");
            request.Headers.Add("X-Api-Key", options.Value.ApiKey);
            using var response = await client.SendAsync(request, budget.Token);
            return response.IsSuccessStatusCode ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded("Optional source unavailable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return HealthCheckResult.Degraded("Optional source unavailable."); }
    }
}
