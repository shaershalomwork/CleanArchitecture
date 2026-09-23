using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Connections;
using CleanArchitecture.Infrastructure.Observability;
using CleanArchitecture.Infrastructure.Sources.CustomerRegistry;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.DependencyInjection;

public static class OracleRegistration
{
    public static void AddOracleCustomerRegistry(this IServiceCollection services)
    {
        services.AddScoped<OracleConnectionFactory>();
        services.AddKeyedScoped<ICustomerSourceAdapter, OracleCustomerSourceAdapter>(CustomerDatabaseProvider.Oracle);
        services.AddKeyedScoped<ICustomerWriteSourceAdapter, OracleCustomerWriteSourceAdapter>(CustomerDatabaseProvider.Oracle);
        services.AddKeyedScoped<IHealthCheck, OracleCustomerRegistryHealthCheck>(CustomerDatabaseProvider.Oracle);
    }
}
