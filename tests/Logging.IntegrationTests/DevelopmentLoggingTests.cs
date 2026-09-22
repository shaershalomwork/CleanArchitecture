using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Net.Sockets;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace CleanArchitecture.Logging.IntegrationTests;

[Category("LoggingIntegration"), NonParallelizable]
public class DevelopmentLoggingTests
{
    [Test]
    public async Task TwoRunsRecreateSecurityAndApplicationStorage()
    {
        if (Environment.GetEnvironmentVariable("RUN_LOGGING_INTEGRATION_TESTS") != "1")
            Assert.Ignore("Set RUN_LOGGING_INTEGRATION_TESTS=1 to run Docker logging acceptance.");
        string? previousCluster = null, previousPassword = null, previousKey = null, previousTrace = null;
        var existing = (await Docker("ps", "-aq")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var baseline = new Dictionary<string, string>();
        foreach (var id in existing) baseline[id] = await Docker("inspect", "--format", "{{.Id}} {{.State.Status}} {{.State.StartedAt}} {{.State.FinishedAt}}", id);
        string? previousCertificate = null;
        for (var run = 1; run <= 2; run++)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(12));
            var ct = deadline.Token;
            var dashboardUrl = "https://localhost:" + FreePort();
            var dashboardToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
                ["--environment=Development",
                    "--ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL=https://localhost:" + FreePort(),
                    "--ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL=https://localhost:" + FreePort(),
                    "--ASPIRE_ALLOW_UNSECURED_TRANSPORT=false", "--ASPIRE_DASHBOARD_FRONTEND_BROWSERTOKEN=" + dashboardToken],
                (options, settings) => { settings.EnvironmentName = "Development"; options.DisableDashboard = false; options.EnableResourceLogging = false; }, ct);
            // The testing builder defaults its dashboard to HTTP after parsing command-line settings.
            // Override the live configuration directly so transport validation and Kestrel use HTTPS.
            builder.Configuration["ASPNETCORE_URLS"] = dashboardUrl;
            builder.Configuration["urls"] = dashboardUrl;
            builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "false";
            // Keep authentication tokens out of test reports; resource output is inspected in memory below.
            builder.Services.AddLogging(logging => logging.ClearProviders());
            var session = Guid.NewGuid().ToString("N");
            foreach (var container in builder.Resources.OfType<ContainerResource>())
                builder.CreateResourceBuilder(container).WithContainerRuntimeArgs("--label", "logging-acceptance=" + session);
            await using var app = await builder.BuildAsync(ct);
            await app.StartAsync(ct);
            var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
            await notifications.WaitForResourceHealthyAsync("elasticsearch", ct);
            TestContext.Progress.WriteLine($"Run {run}: Elasticsearch authenticated health passed.");
            await notifications.WaitForResourceAsync("elasticsearch-init", KnownResourceStates.Exited, ct);
            TestContext.Progress.WriteLine($"Run {run}: Elasticsearch initialization job exited.");
            // WebAPI now starts independently; explicitly wait for logging before checking ingestion.
            await notifications.WaitForResourceHealthyAsync("otel-collector", ct);
            await notifications.WaitForResourceAsync("kibana-init", KnownResourceStates.Exited, ct);
            await notifications.WaitForResourceHealthyAsync("webapi", ct);
            var model = app.Services.GetRequiredService<DistributedApplicationModel>();
            foreach (var name in new[] { "elasticsearch", "elasticsearch-init", "otel-collector", "kibana", "kibana-init" })
                Assert.That(model.Resources.Any(r => r.Name == name), Is.True, "Expected dashboard resource is missing.");
            var password = await model.Resources.OfType<ParameterResource>().Single(p => p.Name == "logging-elastic-password").GetValueAsync(ct);
            using var elastic = new HttpClient { BaseAddress = app.GetEndpoint("elasticsearch", "https") };
            elastic.DefaultRequestHeaders.Authorization = Basic(password!);
            var identity = await GetJson(elastic, "/", ct);
            var cluster = identity.GetProperty("cluster_uuid").GetString();
            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadOnly);
                var certificate = store.Certificates.First(c => c.HasPrivateKey && c.Extensions.Any(e => e.Oid?.Value == "1.3.6.1.4.1.311.84.1.1") && c.NotAfter > DateTime.Now).Thumbprint;
                if (previousCertificate is not null) Assert.That(certificate == previousCertificate, Is.True, "Development certificate changed.");
                previousCertificate = certificate;
            }
            Assert.That(cluster, Is.Not.EqualTo(previousCluster), "A new run must use a new cluster.");
            if (previousPassword is not null) Assert.That(password == previousPassword, Is.True, "Saved password changed.");
            if (previousKey is not null)
            {
                using var oldKey = new HttpRequestMessage(HttpMethod.Get, "/_security/_authenticate");
                oldKey.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", previousKey);
                using var rejected = await elastic.SendAsync(oldKey, ct);
                Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                var oldRecords = await Search(elastic, previousTrace!, ct);
                Assert.That(oldRecords.GetArrayLength(), Is.Zero, "Previous run's logs survived.");
            }
            using (var wrong = new HttpRequestMessage(HttpMethod.Get, "/_security/_authenticate"))
            {
                wrong.Headers.Authorization = Basic("incorrect-password");
                using var response = await elastic.SendAsync(wrong, ct);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            }
            using var collector = new HttpClient { BaseAddress = app.GetEndpoint("otel-collector", "https") };
            foreach (var token in new string?[] { null, "incorrect-token" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/logs") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await collector.SendAsync(request, ct);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            }
            using var kibana = new HttpClient { BaseAddress = app.GetEndpoint("kibana", "https") };
            kibana.DefaultRequestHeaders.Authorization = Basic(password!);
            var view = await GetJson(kibana, "/api/data_views/data_view/application-logs", ct);
            Assert.That(view.GetProperty("data_view").GetProperty("name").GetString(), Is.EqualTo("Application logs"));
            using var web = new HttpClient { BaseAddress = app.GetEndpoint("webapi", "https") };
            using var result = await web.GetAsync("/api/customers?token=sensitive-acceptance-marker", ct);
            var trace = result.Headers.GetValues("X-Correlation-ID").Single();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            JsonElement records;
            do { records = await Search(elastic, trace, ct); }
            while (records.GetArrayLength() == 0 && await timer.WaitForNextTickAsync(ct));
            Assert.That(records.GetArrayLength(), Is.EqualTo(1), "Expected exactly one request completion record.");
            Assert.That(records.GetRawText().Contains("sensitive-acceptance-marker", StringComparison.Ordinal), Is.False, "Sensitive content was exported.");
            foreach (var field in new[] { "service.name", "service.version", "deployment.environment.name", "CorrelationId" })
                Assert.That(records.GetRawText().Contains(field, StringComparison.Ordinal), Is.True, "Required metadata missing: " + field);
            var lifecycle = await GetJson(elastic, "/logs-webapi.otel-development/_ilm/explain", ct);
            Assert.That(lifecycle.GetProperty("indices").EnumerateObject().All(p => p.Value.GetProperty("managed").GetBoolean() && p.Value.GetProperty("policy").GetString() == "webapi-development"), Is.True);
            if (Environment.GetEnvironmentVariable("RUN_LOGGING_BROWSER_TESTS") == "1")
            {
                await BrowserChecks.Kibana(app.GetEndpoint("kibana", "https"), password!, trace);
                TestContext.Progress.WriteLine($"Run {run}: Kibana browser sign-in and correlated Discover search passed with trusted HTTPS.");
            }
            var handoff = model.Resources.Single(r => r.Name == "elasticsearch-init").Annotations
                .OfType<ContainerMountAnnotation>().Single(m => m.Target == "/handoff").Source!;
            previousKey = await File.ReadAllTextAsync(Path.Combine(handoff, "elasticsearch-api-key"), ct);
            previousCluster = cluster; previousPassword = password; previousTrace = trace;
            var resourceLogs = app.Services.GetRequiredService<ResourceLoggerService>();
            var secrets = new List<string> { previousKey, await File.ReadAllTextAsync(Path.Combine(handoff, "kibana-token"), ct) };
            foreach (var parameter in model.Resources.OfType<ParameterResource>())
            {
                var value = await parameter.GetValueAsync(ct);
                if (!string.IsNullOrEmpty(value)) secrets.Add(value);
            }
            foreach (var resource in model.Resources.Where(r => r is ContainerResource or ProjectResource))
            {
                var output = new StringBuilder();
                await foreach (var log in resourceLogs.GetAllAsync(resource)) output.Append(JsonSerializer.Serialize(log));
                var text = output.ToString();
                Assert.That(secrets.Any(secret => text.Contains(secret, StringComparison.Ordinal)), Is.False, "Secret leaked in resource output.");
                Assert.That(text.Contains("sensitive-acceptance-marker", StringComparison.Ordinal), Is.False, "Sensitive marker leaked in resource output.");
                if (resource.Name == "elasticsearch-init") Assert.That(text.Contains("Empty application storage verified", StringComparison.Ordinal), Is.True);
                if (resource.Name == "otel-collector") Assert.That(text.Contains("Initializing new persistent queue", StringComparison.Ordinal), Is.True);
            }
            if (run == 2 && Environment.GetEnvironmentVariable("RUN_LOGGING_BROWSER_TESTS") == "1")
                await BrowserChecks.Dashboard(app.GetEndpoint("aspire-dashboard", "https"), dashboardToken);
            if (run == 2) await CheckQueueRecovery(app, session, elastic, web, ct);
            TestContext.Progress.WriteLine($"Run {run}: cluster {cluster}, certificate {previousCertificate}, trace {trace}; services ready, empty initial storage and queue verified, authenticated TLS and receiver rejection verified, correlated log found.");
            await app.StopAsync(ct);
        }
        foreach (var (id, state) in baseline)
            Assert.That(await Docker("inspect", "--format", "{{.Id}} {{.State.Status}} {{.State.StartedAt}} {{.State.FinishedAt}}", id), Is.EqualTo(state), "Existing Docker resource changed.");
    }

    private static AuthenticationHeaderValue Basic(string password) => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("elastic:" + password)));
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
    private static async Task<JsonElement> GetJson(HttpClient client, string path, CancellationToken ct)
    {
        using var response = await client.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.Clone();
    }
    private static async Task<JsonElement> Search(HttpClient client, string trace, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { query = new { query_string = new { query = $"trace.id:{trace} AND body.text:\"HTTP request completed\"" } } });
        using var readiness = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readiness.CancelAfter(TimeSpan.FromMinutes(1));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            using var response = await client.PostAsync("/logs-webapi.otel-development/_search?ignore_unavailable=true", new StringContent(body, Encoding.UTF8, "application/json"), readiness.Token);
            if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests) continue;
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(readiness.Token));
            return json.RootElement.GetProperty("hits").GetProperty("hits").Clone();
        } while (await timer.WaitForNextTickAsync(readiness.Token));
        throw new InvalidOperationException("Elasticsearch search readiness deadline exceeded.");
    }

    private static async Task CheckQueueRecovery(DistributedApplication app, string session, HttpClient elastic, HttpClient web, CancellationToken ct)
    {
        async Task<string> Container(string name)
        {
            var id = (await Docker("ps", "-q", "--filter", "label=logging-acceptance=" + session, "--filter", "name=^/" + name + "-")).Trim();
            Assert.That(id.Length > 0 && !id.Contains('\n'), Is.True, "Cannot identify the test session container.");
            return id;
        }
        var elasticId = await Container("elasticsearch");
        var collectorId = await Container("otel-collector");
        var traces = new List<string>();
        using var metrics = new HttpClient { BaseAddress = app.GetEndpoint("otel-collector", "metrics") };
        await Docker("stop", elasticId);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (true)
            {
                using var result = await web.GetAsync("/api/customers", ct);
                traces.Add(result.Headers.GetValues("X-Correlation-ID").Single());
                var text = await metrics.GetStringAsync("/metrics", ct);
                if (text.Split('\n').Any(line => line.StartsWith("otelcol_exporter_queue_size{", StringComparison.Ordinal) && double.TryParse(line[(line.LastIndexOf(' ') + 1)..], out var size) && size > 0)) break;
                await timer.WaitForNextTickAsync(ct);
            }
            await Docker("kill", "--signal=SIGKILL", collectorId);
            await Docker("start", collectorId);
        }
        finally { await Docker("start", elasticId); }
        using (var timer = new PeriodicTimer(TimeSpan.FromSeconds(1)))
        {
            while (true)
            {
                try
                {
                    var health = await GetJson(elastic, "/_cluster/health?wait_for_status=yellow&timeout=1s", ct);
                    if (health.GetProperty("status").GetString() is "yellow" or "green") break;
                }
                catch (HttpRequestException) { }
                await timer.WaitForNextTickAsync(ct);
            }
        }
        foreach (var trace in traces)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while ((await Search(elastic, trace, ct)).GetArrayLength() == 0) await timer.WaitForNextTickAsync(ct);
        }
        TestContext.Progress.WriteLine("Disk queue recovered across Collector restart and Elasticsearch downtime.");
    }

    private static async Task<string> Docker(params string[] args)
    {
        var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        await stderr;
        Assert.That(process.ExitCode, Is.Zero, "Docker acceptance operation failed.");
        return await stdout;
    }
}
