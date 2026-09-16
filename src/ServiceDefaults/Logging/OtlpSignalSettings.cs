using Microsoft.Extensions.Configuration;
using OpenTelemetry.Exporter;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace CleanArchitecture.ServiceDefaults.Logging;

public sealed record OtlpSignalSettings(Uri Endpoint, OtlpExportProtocol Protocol, string? Headers, int TimeoutMilliseconds,
    string? Certificate = null, string? ClientCertificate = null, string? ClientKey = null)
{
    public static OtlpSignalSettings? Read(IConfiguration configuration, string signal)
    {
        var prefix = $"OTEL_EXPORTER_OTLP_{signal.ToUpperInvariant()}_";
        var specific = configuration[prefix + "ENDPOINT"];
        var endpoint = specific ?? configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        var protocol = (configuration[prefix + "PROTOCOL"] ?? configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] ?? "grpc") switch
        {
            "grpc" => OtlpExportProtocol.Grpc,
            "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
            _ => throw new InvalidOperationException("OTLP protocol must be grpc or http/protobuf.")
        };
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("OTLP endpoint must be an HTTP(S) URL without credentials, query, or fragment.");
        if (specific is null && protocol == OtlpExportProtocol.HttpProtobuf)
            uri = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/v1/" + signal.ToLowerInvariant());
        var timeout = configuration[prefix + "TIMEOUT"] ?? configuration["OTEL_EXPORTER_OTLP_TIMEOUT"] ?? "10000";
        if (!int.TryParse(timeout, out var milliseconds) || milliseconds <= 0)
            throw new InvalidOperationException("OTLP timeout must be a positive number of milliseconds.");
        return new(uri, protocol, configuration[prefix + "HEADERS"] ?? configuration["OTEL_EXPORTER_OTLP_HEADERS"], milliseconds,
            configuration["OTEL_EXPORTER_OTLP_CERTIFICATE"], configuration["OTEL_EXPORTER_OTLP_CLIENT_CERTIFICATE"],
            configuration["OTEL_EXPORTER_OTLP_CLIENT_KEY"]);
    }

    public void Apply(OtlpExporterOptions options)
    {
        options.Endpoint = Endpoint;
        options.Protocol = Protocol;
        options.Headers = Headers;
        options.TimeoutMilliseconds = TimeoutMilliseconds;
        if (Protocol == OtlpExportProtocol.HttpProtobuf && !string.IsNullOrWhiteSpace(Certificate))
        {
            // OTel 1.18's CA loader calls CreateFromPemFile, which also expects a private
            // key. A trust bundle contains public certificates only. Use the platform TLS
            // chain policy, preserving its normal hostname and validity checks.
            options.HttpClientFactory = () => new HttpClient(new PemTrustHandler(Certificate, ClientCertificate, ClientKey));
        }
    }

    private sealed class PemTrustHandler : DelegatingHandler
    {
        private readonly X509Certificate2Collection _roots = new();
        private readonly X509Certificate2? _client;

        public PemTrustHandler(string caFile, string? clientFile, string? keyFile)
        {
            _roots.ImportFromPemFile(caFile);
            if (_roots.Count == 0) throw new InvalidOperationException("OTLP CA bundle contains no certificates.");
            var chain = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck };
            chain.CustomTrustStore.AddRange(_roots);
            var ssl = new SslClientAuthenticationOptions { CertificateChainPolicy = chain };
            if (!string.IsNullOrWhiteSpace(clientFile))
            {
                _client = X509Certificate2.CreateFromPemFile(clientFile, keyFile);
                ssl.ClientCertificates = new X509CertificateCollection { _client };
            }
            InnerHandler = new SocketsHttpHandler { SslOptions = ssl };
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            foreach (var root in _roots) root.Dispose();
            _client?.Dispose();
        }
    }
}
