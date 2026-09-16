using System.Net;
using System.Text;
using CleanArchitecture.Application.Common.Results;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.IntegrationTests.Execution;
using CleanArchitecture.Infrastructure.Sources.Billing;
using Microsoft.Extensions.Options;
using Shouldly;
namespace CleanArchitecture.Infrastructure.IntegrationTests.Sources;
public class BillingTests
{
    [TestCase(200, "{\"outstandingBalance\":0,\"currency\":\"USD\"}", "Success", "")]
    [TestCase(200, "{\"currency\":\"USD\"}", "Error", "BILLING.INVALID_RESPONSE")]
    [TestCase(200, "not-json", "Error", "BILLING.INVALID_RESPONSE")]
    [TestCase(401, "secret server detail", "Error", "BILLING.AUTHENTICATION_FAILED")]
    [TestCase(404, "", "Error", "BILLING.NOT_FOUND")]
    [TestCase(503, "", "Error", "BILLING.UNAVAILABLE")]
    public async Task InterpretsSourceContract(int status, string body, string outcome, string code)
    {
        using var transport = new StubHandler(status, body);
        using var client = new HttpClient(transport) { BaseAddress = new("https://billing.example/") };
        var adapter = new HttpBillingSourceAdapter(client, SourceExecutorTests.Create(),
            Options.Create(new BillingOptions { ApiKey = "fixture-key" }), new SourceExecutorTests.Correlation(), TimeProvider.System);
        var result = await adapter.GetSummaryAsync("CUST-001", default);
        result.Status.ToString().ShouldBe(outcome);
        if (result.Status == OperationStatus.Success) result.Data.OutstandingBalance.ShouldBe(0);
        else result.Issues[0].Code.ShouldBe(code);
        transport.Correlation.ShouldBe("test-trace");
        transport.Calls.ShouldBe(status == 503 ? 2 : 1);
        result.Issues.ShouldNotContain(i => i.Message.Contains("secret", StringComparison.Ordinal));
    }
    private sealed class StubHandler(int status, string body) : HttpMessageHandler
    {
        public string? Correlation { get; private set; }
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Correlation = request.Headers.GetValues("X-Correlation-ID").Single();
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
