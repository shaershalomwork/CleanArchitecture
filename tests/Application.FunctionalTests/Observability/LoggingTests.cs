using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.ServiceDefaults.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;

namespace CleanArchitecture.Application.FunctionalTests.Observability;

[NonParallelizable]
public class LoggingTests
{
    [TestCase("CERTIFICATE")]
    [TestCase("CLIENT_CERTIFICATE")]
    [TestCase("CLIENT_KEY")]
    public void SignalCertificateSettingsOverrideGenericWithoutAffectingOtherSignals(string setting)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "https://localhost:4318",
            ["OTEL_EXPORTER_OTLP_" + setting] = "shared.pem",
            ["OTEL_EXPORTER_OTLP_LOGS_" + setting] = "logs.pem"
        }).Build();
        static string? Value(OtlpSignalSettings settings, string name) => name switch
        {
            "CERTIFICATE" => settings.Certificate,
            "CLIENT_CERTIFICATE" => settings.ClientCertificate,
            _ => settings.ClientKey
        };
        Value(OtlpSignalSettings.Read(configuration, "logs")!, setting).ShouldBe("logs.pem");
        Value(OtlpSignalSettings.Read(configuration, "traces")!, setting).ShouldBe("shared.pem");
        Value(OtlpSignalSettings.Read(configuration, "metrics")!, setting).ShouldBe("shared.pem");
        configuration["OTEL_EXPORTER_OTLP_LOGS_" + setting] = null;
        Value(OtlpSignalSettings.Read(configuration, "logs")!, setting).ShouldBe("shared.pem");
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void EndpointsAreOptInAndSignalOverridesGeneric(bool generic, bool specific)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = generic ? "https://general.example/otel" : null,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_EXPORTER_OTLP_HEADERS"] = "shared=value",
            ["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"] = specific ? "https://logs.example/v1/logs" : null,
            ["OTEL_EXPORTER_OTLP_LOGS_HEADERS"] = "specific=value"
        }).Build();
        OtlpSignalSettings.Read(configuration, "logs")?.Endpoint.AbsoluteUri.ShouldBe(
            specific ? "https://logs.example/v1/logs" : generic ? "https://general.example/otel/v1/logs" : null);
        OtlpSignalSettings.Read(configuration, "traces")?.Endpoint.AbsoluteUri.ShouldBe(
            generic ? "https://general.example/otel/v1/traces" : null);
        OtlpSignalSettings.Read(configuration, "metrics")?.Endpoint.AbsoluteUri.ShouldBe(
            generic ? "https://general.example/otel/v1/metrics" : null);
        if (generic || specific)
        {
            OtlpSignalSettings.Read(configuration, "logs")!.Headers.ShouldBe("specific=value");
            OtlpSignalSettings.Read(configuration, "logs")!.Protocol.ShouldBe(OtlpExportProtocol.HttpProtobuf);
        }
    }

    [Test]
    public async Task ConsoleAndOtlpShareMetadataAndNeverExportUnapprovedContent()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var receive = listener.GetContextAsync();
        var output = new StringWriter();
        var previousOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_SERVICE_NAME"] = "logging-test", ["Observability:ServiceVersion"] = "1.2.3-test",
                ["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"] = $"http://localhost:{port}/v1/logs",
                ["OTEL_EXPORTER_OTLP_LOGS_PROTOCOL"] = "http/protobuf",
                ["Logging:LogLevel:Default"] = "Information"
            });
            builder.Environment.EnvironmentName = "Test";
            builder.AddServiceDefaults();
            using var host = builder.Build();
            var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SafeTest");
            using var activity = new Activity("logging-test").SetIdFormat(ActivityIdFormat.W3C).Start();
            using (logger.BeginScope(new Dictionary<string, object> { ["Authorization"] = "secret-scope-token" }))
            {
                logger.LogInformation("HTTP request completed with {StatusCode} in {ElapsedMs} ms", 200, 12.5);
                logger.LogError(new InvalidOperationException("secret-exception-text"), "unapproved-secret-{Password}", "secret-password");
            }
            host.Services.GetServices<ILoggerProvider>().OfType<OpenTelemetryLoggerProvider>().Count().ShouldBe(1);
            var flush = Task.Run(() => host.Services.GetRequiredService<LoggerProvider>().ForceFlush(10000));
            var context = await receive.WaitAsync(TimeSpan.FromSeconds(15));
            context.Request.Url!.AbsolutePath.ShouldBe("/v1/logs");
            using var body = new MemoryStream();
            await context.Request.InputStream.CopyToAsync(body);
            context.Response.StatusCode = 200;
            context.Response.Close();
            (await flush).ShouldBeTrue();
            // OTLP protobuf contains string attributes verbatim; raw inspection also catches hidden leakage.
            var wire = Encoding.UTF8.GetString(body.ToArray());
            wire.ShouldContain("logging-test");
            wire.ShouldContain("1.2.3-test");
            wire.ShouldContain("deployment.environment.name");
            wire.ShouldContain(activity.TraceId.ToHexString());
            wire.ShouldContain("InvalidOperationException");
            wire.ShouldNotContain("secret-");
            host.Dispose(); // drains Console's asynchronous writer
            var stdout = output.ToString();
            stdout.ShouldNotContain("secret-");
            var records = stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var applicationRecords = records.Where(r => r.RootElement.GetProperty("Category").GetString() == "SafeTest").ToArray();
                applicationRecords.Length.ShouldBe(2);
                foreach (var record in applicationRecords)
                {
                    record.RootElement.GetProperty("service.name").GetString().ShouldBe("logging-test");
                    record.RootElement.GetProperty("service.version").GetString().ShouldBe("1.2.3-test");
                    record.RootElement.GetProperty("deployment.environment.name").GetString().ShouldBe("Test");
                    record.RootElement.GetProperty("TraceId").GetString().ShouldBe(activity.TraceId.ToHexString());
                    record.RootElement.GetProperty("CorrelationId").GetString().ShouldBe(activity.TraceId.ToHexString());
                }
            }
            finally { foreach (var record in records) record.Dispose(); }
        }
        finally { Console.SetOut(previousOutput); }
    }

    [Test]
    public async Task ConcurrentRequestsKeepCorrelationAndExcludeQueryAndHeaders()
    {
        var capture = new CaptureProcessor();
        using var factory = new LoggingFactory(capture);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "reader");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "secret-untrusted-correlation");
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => client.GetAsync("/api/customers?token=secret-query")));
        var ids = responses.Select(r => r.Headers.GetValues("X-Correlation-ID").Single()).ToArray();
        ids.Distinct().Count().ShouldBe(12);
        foreach (var id in ids)
        {
            var requestLog = capture.Events.Single(e => e.Message.StartsWith("HTTP request completed") && e.Correlation == id);
            requestLog.Trace.ShouldBe(id);
        }
        JsonSerializer.Serialize(capture.Events).ShouldNotContain("secret-");
        foreach (var response in responses) response.Dispose();
    }

    [Test]
    public void StartupLogsHaveNoInventedRequestIds()
    {
        var result = SafeLogContent.Project(null, null, null);
        result.Attributes["TraceId"].ShouldBeNull();
        result.Attributes["CorrelationId"].ShouldBeNull();
    }

    private sealed class CaptureProcessor : BaseProcessor<LogRecord>
    {
        public ConcurrentQueue<(string Message, string? Trace, string? Correlation)> Events { get; } = new();
        public override void OnEnd(LogRecord data)
        {
            var attributes = data.Attributes!.ToDictionary(p => p.Key, p => p.Value);
            Events.Enqueue((data.FormattedMessage!, attributes["TraceId"] as string, attributes["CorrelationId"] as string));
        }
    }
    private sealed class LoggingFactory(CaptureProcessor capture) : WebApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(logging => logging.AddOpenTelemetry(options => options.AddProcessor(capture)));
        }
    }
}
