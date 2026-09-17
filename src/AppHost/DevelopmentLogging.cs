#pragma warning disable ASPIRECERTIFICATES001 // Pinned Aspire 13.5.3 certificate callbacks.
using System.Net.Http.Headers;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.AppHost;

internal sealed class DevelopmentLogging : IDisposable
{
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private readonly string _handoff;

    private DevelopmentLogging(string root)
    {
        _handoff = Path.Combine(root, ".local", "aspire-logging", Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(_handoff).Create(security);
        }
        else Directory.CreateDirectory(_handoff, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static DevelopmentLogging? Configure(IDistributedApplicationBuilder builder, IResourceBuilder<ProjectResource> web)
    {
        if (!builder.Environment.IsDevelopment() || !builder.ExecutionContext.IsRunMode) return null;
        CheckCertificate();
        var root = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../.."));
        var run = new DevelopmentLogging(root);
        try { run.AddResources(builder, web, root); return run; }
        catch { run.Dispose(); throw; }
    }

    private static void CheckCertificate()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        foreach (var cert in store.Certificates)
        {
            using (cert)
            {
                if (!cert.HasPrivateKey || !cert.Extensions.Any(e => e.Oid?.Value == "1.3.6.1.4.1.311.84.1.1")) continue;
                if (cert.NotBefore > DateTime.Now || cert.NotAfter <= DateTime.Now) continue;
                if (!cert.MatchesHostname("localhost") || !cert.MatchesHostname("elasticsearch.dev.localhost")) continue;
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                if (chain.Build(cert)) return;
            }
        }
        throw new InvalidOperationException("Development logging requires a trusted .NET 10 HTTPS certificate covering localhost and *.dev.localhost. Run 'dotnet dev-certs https --trust' before starting AppHost.");
    }

    private void AddResources(IDistributedApplicationBuilder builder, IResourceBuilder<ProjectResource> web, string root)
    {
        builder.Services.AddHostedService<StartupDeadline>();
        IResourceBuilder<ParameterResource> Saved(string name) => builder.AddParameter(name,
            new GenerateParameterDefault { MinLength = 48, Special = false }, secret: true, persist: true);
        var password = Saved("logging-elastic-password");
        var securityKey = Saved("logging-kibana-security-key");
        var savedObjectsKey = Saved("logging-kibana-objects-key");
        var reportingKey = Saved("logging-kibana-reporting-key");
        var freshToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var receiverToken = builder.AddResource(new ParameterResource("logging-receiver-token", _ => freshToken, secret: true));
        var elastic = builder.AddContainer("elasticsearch", "docker.elastic.co/elasticsearch/elasticsearch", "9.5.4")
            .WithContainerCertificatePaths("/usr/share/elasticsearch/config/aspire-certs")
            .WithContainerNetworkAlias("elasticsearch.dev.localhost")
            .WithHttpsEndpoint(targetPort: 9200)
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("ELASTIC_PASSWORD_FILE", "/run/secrets/elastic-password")
            .WithEnvironment("xpack.security.enabled", "true")
            .WithEnvironment("xpack.security.http.ssl.enabled", "true")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms1g -Xmx1g")
            .WithContainerFiles("/run/secrets", async (_, ct) => [Secret("elastic-password", await password.Resource.GetValueAsync(ct), 1000)])
            .WithHttpsCertificateConfiguration(ctx =>
            {
                ctx.EnvironmentVariables["xpack.security.http.ssl.certificate"] = ctx.CertificatePath;
                ctx.EnvironmentVariables["xpack.security.http.ssl.key"] = ctx.KeyPath;
                return Task.CompletedTask;
            }).WithHttpsDeveloperCertificate();
        AddHealth(builder, elastic, password, "/_cluster/health?wait_for_status=yellow&timeout=5s", "status", "yellow", "green");

        IResourceBuilder<ContainerResource> Init(string name, string mode) => builder.AddContainer(name, "node", "24.13.0-bookworm-slim")
            .WithEntrypoint("node").WithArgs("/setup/development.mjs", mode)
            .WithContainerFiles("/setup", Path.Combine(root, "deploy/elasticsearch"))
            .WithContainerFiles("/run/secrets", async (_, ct) => [Secret("elastic-password", await password.Resource.GetValueAsync(ct), 0)])
            .WithEnvironment("ELASTICSEARCH_ENDPOINT", "https://elasticsearch.dev.localhost:9200")
            .WithDeveloperCertificateTrust(true).WithCertificateTrustConfiguration(ctx =>
            {
                ctx.EnvironmentVariables["LOG_CA_FILE"] = ctx.CertificateBundlePath;
                return Task.CompletedTask;
            });
        var elasticInit = Init("elasticsearch-init", "elasticsearch")
            .WithBindMount(_handoff, "/handoff").WaitFor(elastic, WaitBehavior.StopOnResourceUnavailable);

        var collector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib", "0.153.0")
            .WithContainerNetworkAlias("collector.dev.localhost")
            .WithContainerRuntimeArgs("--user", "10001:0")
            .WithHttpsEndpoint(targetPort: 4318)
            .WithHttpEndpoint(targetPort: 13133, name: "health")
            .WithHttpEndpoint(targetPort: 8888, name: "metrics")
            .WithArgs("--config=/etc/otelcol/config.yaml", "--config=/etc/otelcol/aspire.yaml")
            .WithContainerFiles("/etc/otelcol", Path.Combine(root, "deploy/collector"))
            .WithContainerFiles("/var/lib", [new ContainerDirectory { Name = "otelcol", Owner = 10001, Group = 0,
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute }])
            .WithContainerFiles("/run/secrets", async (_, ct) => [
                Secret("elasticsearch-api-key", await ReadHandoff("elasticsearch-api-key", ct), 10001),
                Secret("receiver-token", await receiverToken.Resource.GetValueAsync(ct), 10001)])
            .WithEnvironment("ELASTICSEARCH_ENDPOINT", "https://elasticsearch.dev.localhost:9200")
            .WithEnvironment("LOG_ENVIRONMENT", "development")
            .WithHttpsCertificateConfiguration(ctx =>
            {
                ctx.EnvironmentVariables["LOG_TLS_CERT"] = ctx.CertificatePath;
                ctx.EnvironmentVariables["LOG_TLS_KEY"] = ctx.KeyPath;
                return Task.CompletedTask;
            }).WithHttpsDeveloperCertificate()
            .WithDeveloperCertificateTrust(true).WithCertificateTrustConfiguration(ctx =>
            {
                ctx.EnvironmentVariables["LOG_CA_FILE"] = ctx.CertificateBundlePath;
                return Task.CompletedTask;
            }).WaitForCompletion(elasticInit).WithHttpHealthCheck("/", endpointName: "health");

        var cookie = "logging_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..16].ToLowerInvariant();
        var kibana = builder.AddContainer("kibana", "docker.elastic.co/kibana/kibana", "9.5.4")
            .WithContainerNetworkAlias("kibana.dev.localhost")
            .WithHttpsEndpoint(targetPort: 5601)
            .WithEnvironment("ELASTICSEARCH_HOSTS", "[\"https://elasticsearch.dev.localhost:9200\"]")
            .WithEnvironment("SERVER_HOST", "0.0.0.0")
            .WithEnvironment("SERVER_SSL_ENABLED", "true")
            .WithContainerFiles("/usr/share/kibana/config", async (_, ct) => [Secret("kibana.yml",
                "elasticsearch.serviceAccountToken: " + JsonSerializer.Serialize(await ReadHandoff("kibana-token", ct)) + "\n" +
                "xpack.security.encryptionKey: " + JsonSerializer.Serialize(await securityKey.Resource.GetValueAsync(ct)) + "\n" +
                "xpack.encryptedSavedObjects.encryptionKey: " + JsonSerializer.Serialize(await savedObjectsKey.Resource.GetValueAsync(ct)) + "\n" +
                "xpack.reporting.encryptionKey: " + JsonSerializer.Serialize(await reportingKey.Resource.GetValueAsync(ct)) + "\n" +
                "xpack.security.cookieName: " + cookie + "\n", 1000)])
            .WithHttpsCertificateConfiguration(ctx =>
            {
                ctx.EnvironmentVariables["SERVER_SSL_CERTIFICATE"] = ctx.CertificatePath;
                ctx.EnvironmentVariables["SERVER_SSL_KEY"] = ctx.KeyPath;
                return Task.CompletedTask;
            }).WithHttpsDeveloperCertificate()
            .WithDeveloperCertificateTrust(true).WithCertificateTrustConfiguration(ctx =>
            {
                ctx.EnvironmentVariables["ELASTICSEARCH_SSL_CERTIFICATEAUTHORITIES"] = ctx.CertificateBundlePath;
                return Task.CompletedTask;
            }).WaitForCompletion(elasticInit);
        AddHealth(builder, kibana, password, "/api/status", "status.overall.level", "available");
        var kibanaInit = Init("kibana-init", "kibana")
            .WithEnvironment("KIBANA_ENDPOINT", "https://kibana.dev.localhost:5601").WaitFor(kibana, WaitBehavior.StopOnResourceUnavailable);
        web.WaitFor(collector).WaitForCompletion(kibanaInit)
            .WithEnvironment("OTEL_BLRP_MAX_QUEUE_SIZE", "8192")
            .WithEnvironment("OTEL_BLRP_MAX_EXPORT_BATCH_SIZE", "512")
            .WithEnvironment("OTEL_BLRP_SCHEDULE_DELAY", "1000")
            .WithEnvironment("OTEL_EXPORTER_OTLP_LOGS_ENDPOINT", ReferenceExpression.Create($"{collector.GetEndpoint("https")}/v1/logs"))
            .WithEnvironment("OTEL_EXPORTER_OTLP_LOGS_PROTOCOL", "http/protobuf")
            .WithEnvironment("OTEL_EXPORTER_OTLP_LOGS_HEADERS", ReferenceExpression.Create($"Authorization=Bearer {receiverToken}"))
            .WithDeveloperCertificateTrust(true).WithCertificateTrustConfiguration(ctx =>
            {
                ctx.EnvironmentVariables["OTEL_EXPORTER_OTLP_LOGS_CERTIFICATE"] = ctx.CertificateBundlePath;
                return Task.CompletedTask;
            });
    }

    private static ContainerFile Secret(string name, string? value, int owner) => new()
    {
        Name = name, Contents = value ?? throw new InvalidOperationException("Required logging credential is unavailable."),
        Owner = owner, Group = 0, Mode = PrivateFile
    };

    private async Task<string> ReadHandoff(string name, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var file = Path.Combine(_handoff, name);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        while (!File.Exists(file)) await timer.WaitForNextTickAsync(timeout.Token);
        return await File.ReadAllTextAsync(file, timeout.Token);
    }

    private static void AddHealth(IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource> resource,
        IResourceBuilder<ParameterResource> password, string path, string property, params string[] accepted)
    {
        var name = resource.Resource.Name + "-authenticated";
        builder.Services.AddHealthChecks().AddCheck(name, new LoggingHealthCheck(resource.GetEndpoint("https"), password.Resource, path, property, accepted));
        resource.WithHealthCheck(name);
    }

    public void Dispose()
    {
        // This exact randomly named directory was created by this instance. Never enumerate siblings.
        if (Directory.Exists(_handoff)) Directory.Delete(_handoff, recursive: true);
    }

    private sealed class StartupDeadline(ResourceNotificationService notifications, IHostApplicationLifetime lifetime,
        ILogger<StartupDeadline> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(10));
            try
            {
                await notifications.WaitForResourceHealthyAsync("elasticsearch", WaitBehavior.StopOnResourceUnavailable, deadline.Token);
                await notifications.WaitForResourceHealthyAsync("otel-collector", WaitBehavior.StopOnResourceUnavailable, deadline.Token);
                await notifications.WaitForResourceHealthyAsync("kibana", WaitBehavior.StopOnResourceUnavailable, deadline.Token);
                await notifications.WaitForResourceAsync("kibana-init", KnownResourceStates.Exited, deadline.Token);
                if (!notifications.TryGetCurrentState("kibana-init", out var completed) || completed.Snapshot.ExitCode != 0)
                    throw new InvalidOperationException();
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError("Development logging startup failed or exceeded ten minutes. Check resource health, initialization output and HTTPS trust.");
                lifetime.StopApplication();
            }
        }
    }

    private sealed class LoggingHealthCheck(EndpointReference endpoint, ParameterResource password, string path, string property, string[] accepted) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes("elastic:" + await password.GetValueAsync(cancellationToken))));
                using var response = await client.GetAsync(new Uri(new Uri(endpoint.Url), path), cancellationToken);
                if (!response.IsSuccessStatusCode) return HealthCheckResult.Unhealthy("Service authentication or readiness failed.");
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var value = json.RootElement;
                foreach (var segment in property.Split('.')) value = value.GetProperty(segment);
                return accepted.Contains(value.GetString()) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Service is not ready.");
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException or JsonException or KeyNotFoundException)
            { return HealthCheckResult.Unhealthy("Service is not ready; check HTTPS trust and initialization output."); }
        }
    }
}
