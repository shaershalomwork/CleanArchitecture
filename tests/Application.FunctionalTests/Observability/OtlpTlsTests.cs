using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CleanArchitecture.ServiceDefaults.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;

namespace CleanArchitecture.Application.FunctionalTests.Observability;

public class OtlpTlsTests
{
    [TestCase("trusted", OtlpExportProtocol.HttpProtobuf)]
    [TestCase("wrong-ca", OtlpExportProtocol.HttpProtobuf)]
    [TestCase("wrong-host", OtlpExportProtocol.HttpProtobuf)]
    [TestCase("expired", OtlpExportProtocol.HttpProtobuf)]
    [TestCase("trusted", OtlpExportProtocol.Grpc)]
    [TestCase("wrong-ca", OtlpExportProtocol.Grpc)]
    [TestCase("wrong-host", OtlpExportProtocol.Grpc)]
    [TestCase("expired", OtlpExportProtocol.Grpc)]
    public async Task PublicCaBundlePreservesTrustAndHostnameValidation(string scenario, OtlpExportProtocol protocol)
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=Logging test CA", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=Logging test server", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        leafRequest.CertificateExtensions.Add(san.Build());
        using var publicLeaf = leafRequest.Create(root, DateTimeOffset.UtcNow.AddMinutes(-4),
            scenario == "expired" ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddHours(1), RandomNumberGenerator.GetBytes(16));
        using var ephemeralCertificate = publicLeaf.CopyWithPrivateKey(leafKey);
        // Schannel needs a persisted key when Kestrel acts as a TLS server on Windows.
        using var serverCertificate = X509CertificateLoader.LoadPkcs12(ephemeralCertificate.Export(X509ContentType.Pfx), null,
            X509KeyStorageFlags.Exportable);
        using var wrongKey = RSA.Create(2048);
        var wrongRequest = new CertificateRequest("CN=Unrelated CA", wrongKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var wrongRoot = wrongRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        var caFile = Path.Combine(Path.GetTempPath(), "logging-ca-" + Guid.NewGuid().ToString("N") + ".pem");
        await File.WriteAllTextAsync(caFile, (scenario == "wrong-ca" ? wrongRoot : root).ExportCertificatePem());
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(serverCertificate)));
            await using var server = builder.Build();
            server.MapGet("/", () => "OK");
            await server.StartAsync();
            var address = server.Urls.Single();
            if (scenario == "wrong-host") address = address.Replace("127.0.0.1", "localhost");
            var settings = new OtlpSignalSettings(new Uri(address), protocol, null, 5000, caFile);
            var options = new OtlpExporterOptions();
            settings.Apply(options);
            using var client = options.HttpClientFactory();
            if (scenario == "trusted") (await client.GetStringAsync(address)).ShouldBe("OK");
            else await Should.ThrowAsync<HttpRequestException>(() => client.GetStringAsync(address));
            await server.StopAsync();
        }
        finally { File.Delete(caFile); }
    }
}
