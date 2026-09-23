using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Connections;
using CleanArchitecture.Infrastructure.Observability;
using CleanArchitecture.Infrastructure.Sources.CustomerRegistry;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.DependencyInjection;

public static class SQLiteRegistration
{
    public static void AddSqliteCustomerRegistry(this IServiceCollection services)
    {
        services.AddScoped<SqliteConnectionFactory>();
        services.AddScoped<SqliteWriteConnectionFactory>();
        services.AddKeyedScoped<ICustomerSourceAdapter, SqliteCustomerSourceAdapter>(CustomerDatabaseProvider.SQLite);
        services.AddKeyedScoped<ICustomerWriteSourceAdapter, SqliteCustomerWriteSourceAdapter>(CustomerDatabaseProvider.SQLite);
        services.AddKeyedScoped<IHealthCheck, SQLiteCustomerRegistryHealthCheck>(CustomerDatabaseProvider.SQLite);
    }
}
