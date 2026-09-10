using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CleanArchitecture.Infrastructure.IntegrationTests.Sources;

public class CustomerConfigurationTests
{
    [TestCase("SqlServer", "Live", "Sql")]
    [TestCase("SQLite", "Live", "Sqlite")]
    [TestCase("Oracle", "Live", "Oracle")]
    [TestCase("SqlServer", "Fake", "Fake")]
    [TestCase("SQLite", "Fake", "Fake")]
    [TestCase("Oracle", "Fake", "Fake")]
    public void ResolvesBothCapabilitiesWithoutOpeningConnections(string provider, string mode, string prefix)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Test" });
        builder.Configuration["Sources:CustomerRegistry:Provider"] = provider;
        builder.Configuration["Sources:CustomerRegistry:Mode"] = mode;
        builder.Configuration["Sources:CustomerRegistry:ConnectionName"] = "CustomName";
        if (mode == "Live") builder.Configuration["ConnectionStrings:CustomName"] = "Data Source=not-contacted";
        builder.Configuration["Sources:Billing:Mode"] = "Fake";
        builder.AddInfrastructureServices();
        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICustomerSourceAdapter>().GetType().Name.ShouldBe(prefix + "CustomerSourceAdapter");
        scope.ServiceProvider.GetRequiredService<ICustomerWriteSourceAdapter>().GetType().Name.ShouldBe(prefix + "CustomerWriteSourceAdapter");
    }

    [TestCase("Test", "Oracle", "Live")]
    [TestCase("Test", "Unknown", "Fake")]
    [TestCase("Production", "Oracle", "Fake")]
    public void RejectsMissingLiveConnectionsUnknownProvidersAndProductionFakes(string environment, string provider, string mode)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        builder.Configuration["Sources:CustomerRegistry:Provider"] = provider;
        builder.Configuration["Sources:CustomerRegistry:Mode"] = mode;
        builder.Configuration["Sources:CustomerRegistry:ConnectionName"] = "NotConfigured";
        builder.AddInfrastructureServices();
        using var host = builder.Build();
        // Invalid enum text fails binding; other invalid settings fail options validation.
        Should.Throw<Exception>(() => host.Services.GetRequiredService<IOptions<CustomerRegistryOptions>>().Value);
    }
}
