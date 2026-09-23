using Shouldly;
using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.IntegrationTests.Sources;

public sealed class DevelopmentSourcesTests
{
    private static HostApplicationBuilder Create(string environment, string customerMode = "Fake", string billingMode = "Fake")
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        builder.Configuration["Sources:CustomerRegistry:Mode"] = customerMode;
        builder.Configuration["Sources:CustomerRegistry:Provider"] = "None";
        builder.Configuration["Sources:Billing:Mode"] = billingMode;
        builder.Configuration["Sources:Billing:BaseUrl"] = "https://billing.example.invalid/";
        builder.Configuration["Sources:Billing:ApiKey"] = "fixture";
        builder.AddInfrastructureServices();
        return builder;
    }

    [Test] public async Task DevelopmentStartsWithoutConnectionsAndHasAnEmptyPerHostRegistry()
    {
        using (var first = Create("Development").Build())
        {
            await first.StartAsync();
            using var scope = first.Services.CreateScope();
            var reader = scope.ServiceProvider.GetRequiredService<ICustomerSourceAdapter>();
            reader.ShouldBeOfType<FakeCustomerSourceAdapter>();
            (await reader.GetCustomersAsync(default)).Data.ShouldBeEmpty();
            var writer = scope.ServiceProvider.GetRequiredService<ICustomerWriteSourceAdapter>();
            (await writer.CreateAsync("LOCAL-1", "Sample", default)).HasData.ShouldBeTrue();
            (await reader.GetCustomersAsync(default)).Data.Count.ShouldBe(1);
            await first.StopAsync();
        }
        using var second = Create("Development").Build();
        await second.StartAsync();
        using var secondScope = second.Services.CreateScope();
        (await secondScope.ServiceProvider.GetRequiredService<ICustomerSourceAdapter>().GetCustomersAsync(default)).Data.ShouldBeEmpty();
        await second.StopAsync();
    }

    [TestCase("Test")]
    [TestCase("Staging")]
    [TestCase("Production")]
    public void FakeSourcesAreRejectedOutsideDevelopment(string environment)
    {
        using var host = Create(environment).Build();
        Should.Throw<OptionsValidationException>(() => host.Services.GetRequiredService<IOptions<CustomerRegistryOptions>>().Value)
            .Message.ShouldContain("restricted to Development");
        Should.Throw<OptionsValidationException>(() => host.Services.GetRequiredService<IOptions<BillingOptions>>().Value)
            .Message.ShouldContain("restricted to Development");
    }

    [Test] public void LiveModeRequiresAnInstalledProviderEvenWhenAConnectionStringExists()
    {
        var builder = Create("Development", customerMode: "Live");
        builder.Configuration["ConnectionStrings:CustomerRegistry"] = "not-contacted";
        using var host = builder.Build();
        Should.Throw<OptionsValidationException>(() => host.Services.GetRequiredService<IOptions<CustomerRegistryOptions>>().Value)
            .Message.ShouldContain("optional database project");
    }
}
