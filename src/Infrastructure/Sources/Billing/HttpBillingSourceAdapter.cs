using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Customers.Sources;
using CleanArchitecture.Infrastructure.Configuration;
using CleanArchitecture.Infrastructure.Execution;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Sources.Billing;

public sealed class HttpBillingSourceAdapter(HttpClient client, SourceExecutor executor, IOptions<BillingOptions> options,
    ICorrelationContext correlation, TimeProvider clock) : IBillingSourceAdapter
{
    public Task<OperationResult<BillingSummary>> GetSummaryAsync(string customerId, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(new("BILLING", "GetSummary", IsReadOnly: true), async token =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "customers/" + Uri.EscapeDataString(customerId) + "/summary");
            request.Headers.Add("X-Correlation-ID", correlation.Id);
            request.Headers.Add("X-Api-Key", options.Value.ApiKey);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new SourceFailureException("AUTHENTICATION_FAILED");
            if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout)
                throw new SourceFailureException("TIMEOUT", true);
            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new SourceFailureException("UNAVAILABLE", true);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return OperationResult<BillingSummary>.Failure(new OperationIssue("BILLING.NOT_FOUND", "No billing summary exists for this customer.", IssueCategory.Business, correlation.Id));
            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
                return OperationResult<BillingSummary>.Failure(new OperationIssue("BILLING.REJECTED", "The billing source rejected this operation.", IssueCategory.Business, correlation.Id));
            if (!response.IsSuccessStatusCode) throw new SourceFailureException("INVALID_RESPONSE");
            var payload = await response.Content.ReadFromJsonAsync<BillingPayload>(cancellationToken: token);
            if (payload?.OutstandingBalance is null || payload.Currency is null || payload.Currency.Length != 3 ||
                !payload.Currency.All(c => c is >= 'A' and <= 'Z'))
                throw new SourceFailureException("INVALID_RESPONSE");
            return OperationResult<BillingSummary>.Success(new(payload.OutstandingBalance.Value, payload.Currency, clock.GetUtcNow()));
        }, cancellationToken);

    private sealed record BillingPayload(decimal? OutstandingBalance, string? Currency);
}
