using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Connections;
using CleanArchitecture.Infrastructure.Observability;
using CleanArchitecture.Infrastructure.Sources.CustomerRegistry;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.DependencyInjection;

public static class SqlServerRegistration
{
    public static void AddSqlServerCustomerRegistry(this IServiceCollection services)
    {
        services.AddScoped<SqlConnectionFactory>();
        services.AddKeyedScoped<ICustomerSourceAdapter, SqlCustomerSourceAdapter>(CustomerDatabaseProvider.SqlServer);
        services.AddKeyedScoped<ICustomerWriteSourceAdapter, SqlCustomerWriteSourceAdapter>(CustomerDatabaseProvider.SqlServer);
        services.AddKeyedScoped<IHealthCheck, SqlServerCustomerRegistryHealthCheck>(CustomerDatabaseProvider.SqlServer);
    }
}
