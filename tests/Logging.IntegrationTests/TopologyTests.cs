using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace CleanArchitecture.Logging.IntegrationTests;

public class TopologyTests
{
    [Test, Category("LoggingIntegration"), Category("Browser")]
    public async Task SecuredDashboardIsReachable()
    {
        if (Environment.GetEnvironmentVariable("RUN_LOGGING_BROWSER_TESTS") != "1") Assert.Ignore("Opt-in dashboard browser check.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var dashboardUrl = "https://localhost:" + FreePort();
        var dashboardToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            ["--environment=Test", "--ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL=https://localhost:" + FreePort(),
                "--ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL=https://localhost:" + FreePort(),
                "--ASPIRE_ALLOW_UNSECURED_TRANSPORT=false", "--ASPIRE_DASHBOARD_FRONTEND_BROWSERTOKEN=" + dashboardToken],
            (options, settings) => { settings.EnvironmentName = "Test"; options.DisableDashboard = false; options.EnableResourceLogging = false; }, timeout.Token);
        builder.Configuration["ASPNETCORE_URLS"] = dashboardUrl;
        builder.Configuration["urls"] = dashboardUrl;
        builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "false";
        builder.Services.AddLogging(logging => logging.ClearProviders());
        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await BrowserChecks.Dashboard(app.GetEndpoint("aspire-dashboard", "https"), dashboardToken, ["webapi"], expectTrace: false);
    }

    [TestCase("Test")]
    [TestCase("Production")]
    public async Task NonDevelopmentHostsDoNotAddLoggingContainers(string environment)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            ["--environment=" + environment], (_, settings) => settings.EnvironmentName = environment, timeout.Token);
        Assert.That(builder.Resources.OfType<ContainerResource>(), Is.Empty);
        Assert.That(builder.Resources.Any(r => r.Name == "webapi"), Is.True);
        Assert.That(builder.Resources.OfType<ParameterResource>(), Is.Empty);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
