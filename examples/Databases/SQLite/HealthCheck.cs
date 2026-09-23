using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Connections;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Observability;

public sealed class SQLiteCustomerRegistryHealthCheck(IServiceScopeFactory scopes, IOptions<CustomerRegistryOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(3));
        try
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return HealthCheckResult.Unhealthy("Required source unavailable."); }
    }
}
