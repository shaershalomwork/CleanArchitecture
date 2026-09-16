using CleanArchitecture.Application.Common.Results;
using CleanArchitecture.Application.Customers.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

internal static class CustomerFixtures
{
    // Opt in per test; a new application host must remain an empty registry.
    public static async Task RegisterAsync(IServiceProvider services, params string[] ids)
    {
        using var scope = services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<ICustomerWriteSourceAdapter>();
        foreach (var id in ids)
            (await writer.CreateAsync(id, "Example Customer", default)).Status.ShouldBe(OperationStatus.Success);
    }
}
