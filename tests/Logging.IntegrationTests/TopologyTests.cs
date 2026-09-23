using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Security.Cryptography;
using Aspire.Hosting;
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
            ["--environment=Development", "--ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL=https://localhost:" + FreePort(),
                "--ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL=https://localhost:" + FreePort(),
                "--ASPIRE_ALLOW_UNSECURED_TRANSPORT=false", "--ASPIRE_DASHBOARD_FRONTEND_BROWSERTOKEN=" + dashboardToken],
            (options, settings) => { settings.EnvironmentName = "Development"; options.DisableDashboard = false; options.EnableResourceLogging = false; }, timeout.Token);
        builder.Configuration["ASPNETCORE_URLS"] = dashboardUrl;
        builder.Configuration["urls"] = dashboardUrl;
        builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "false";
        builder.Services.AddLogging(logging => logging.ClearProviders());
        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await BrowserChecks.Dashboard(app.GetEndpoint("aspire-dashboard", "https"), dashboardToken, ["webapi"], expectTrace: false);
    }

    [Test, Category("LoggingIntegration"), NonParallelizable]
    public async Task WebApiStartsAndWritesConsoleLogsWhileLoggingContainersAreStopped()
    {
        if (Environment.GetEnvironmentVariable("RUN_LOGGING_INTEGRATION_TESTS") != "1")
            Assert.Ignore("Opt-in Aspire startup check with logging containers held stopped.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ct = timeout.Token;
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            ["--environment=Development"],
            (options, settings) => { settings.EnvironmentName = "Development"; options.EnableResourceLogging = false; }, ct);
        builder.Services.AddLogging(logging => logging.ClearProviders());
        var containers = builder.Resources.OfType<ContainerResource>().ToArray();
        Assert.That(containers, Has.Length.EqualTo(5), "This check requires the development logging certificate and resources.");
        // Keep every logging dependency unavailable without pulling images or requiring Docker.
        foreach (var container in containers) builder.CreateResourceBuilder(container).WithExplicitStart();
        await using var app = await builder.BuildAsync(ct);
        await app.StartAsync(ct);
        var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
        await notifications.WaitForResourceHealthyAsync("webapi", ct);
        using var web = new HttpClient { BaseAddress = app.GetEndpoint("webapi", "https") };
        using var alive = await web.GetAsync("/alive", ct);
        Assert.That(alive.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var request = await web.GetAsync("/scalar", ct);
        Assert.That(request.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var correlation = request.Headers.GetValues("X-Correlation-ID").Single();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var webResource = model.Resources.Single(r => r.Name == "webapi");
        var logs = app.Services.GetRequiredService<ResourceLoggerService>();
        await foreach (var batch in logs.WatchAsync(webResource).WithCancellation(ct))
        {
            var output = JsonSerializer.Serialize(batch);
            if (output.Contains("HTTP request completed", StringComparison.Ordinal) && output.Contains(correlation, StringComparison.Ordinal))
                return;
        }
        Assert.Fail("The request completion log was not written to the WebAPI console.");
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
