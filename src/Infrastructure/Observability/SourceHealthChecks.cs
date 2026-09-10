using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Connections;
using Dapper;
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
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            if (options.Value.Provider == CustomerDatabaseProvider.Oracle)
            {
                await using var connection = await scope.ServiceProvider.GetRequiredService<OracleConnectionFactory>()
                    .OpenAsync(options.Value.ConnectionName, budget.Token);
                await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1 FROM DUAL", commandTimeout: 3, cancellationToken: budget.Token));
                return HealthCheckResult.Healthy();
            }
            if (options.Value.Provider == CustomerDatabaseProvider.SQLite)
            {
                await Task.Run(() =>
                {
                    using var connection = scope.ServiceProvider.GetRequiredService<SqliteConnectionFactory>()
                        .Open(options.Value.ConnectionName, budget.Token);
                    connection.ExecuteScalar<int>("SELECT 1", commandTimeout: connection.DefaultTimeout);
                    budget.Token.ThrowIfCancellationRequested();
                }, budget.Token);
                return HealthCheckResult.Healthy();
            }
            await using var lease = await scope.ServiceProvider.GetRequiredService<SqlConnectionFactory>()
                .OpenAsync(options.Value.ConnectionName, budget.Token);
            await lease.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", commandTimeout: 3, cancellationToken: budget.Token));
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return HealthCheckResult.Unhealthy("Required source unavailable."); }
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
