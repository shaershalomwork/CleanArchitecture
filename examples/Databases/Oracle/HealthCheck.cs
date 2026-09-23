using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Connections;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Observability;

public sealed class OracleCustomerRegistryHealthCheck(IServiceScopeFactory scopes, IOptions<CustomerRegistryOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await using var connection = await scope.ServiceProvider.GetRequiredService<OracleConnectionFactory>()
                .OpenAsync(options.Value.ConnectionName, budget.Token);
            await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1 FROM DUAL", commandTimeout: 3, cancellationToken: budget.Token));
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return HealthCheckResult.Unhealthy("Required source unavailable."); }
    }
}
