using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.Application.Common.Results;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Web.Contracts;
using CleanArchitecture.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.Customers;

public class OpenApiContractTests
{
    [Test] public async Task DocumentsEveryHandlerAndPreservesExistingContracts()
    {
        await using var factory = new WebApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost") });
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");
        foreach (var path in paths.EnumerateObject())
        foreach (var operation in path.Value.EnumerateObject())
        {
            operation.Value.GetProperty("summary").GetString().ShouldNotBeNullOrWhiteSpace();
            operation.Value.GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace();
        }
        foreach (var (route, verb, name, codes) in new[]
        {
            ("/api/customers", "get", "GetCustomers", "200,400,401,403,500,502,503,504"),
            ("/api/customers/{customerId}/overview", "get", "GetCustomerOverview", "200,400,401,403,404,422,500,502,503,504"),
            ("/api/customers", "post", "CreateCustomer", "200,400,401,403,409,500,502,503,504"),
            ("/api/customers/{customerId}", "put", "ReplaceCustomer", "200,400,401,403,404,500,502,503,504"),
            ("/api/customers/{customerId}", "patch", "PatchCustomer", "200,400,401,403,404,500,502,503,504"),
            ("/api/customers/{customerId}", "delete", "DeleteCustomer", "200,400,401,403,404,500,502,503,504"),
            ("/auth/login", "get", "Login", "200,400"),
            ("/auth/logout", "post", "Logout", "200,400,401,403"),
            ("/auth/me", "get", "Me", "200,400,401,403"),
            ("/auth/antiforgery", "get", "Antiforgery", "200,400")
        })
        {
            var operation = paths.GetProperty(route).GetProperty(verb);
            operation.GetProperty("operationId").GetString().ShouldBe(name);
            var responses = operation.GetProperty("responses");
            responses.EnumerateObject().Select(p => p.Name).Order().ShouldBe(codes.Split(',').Order());
            if (!route.StartsWith("/api/", StringComparison.Ordinal)) continue;
            var schema = name == "GetCustomerOverview" ? "CustomerOverviewResponse"
                : name == "DeleteCustomer" ? "CustomerDeletionResponse" : name == "GetCustomers" ? "IReadOnlyListOfCustomerResponse" : "CustomerResponse";
            foreach (var response in responses.EnumerateObject())
                response.Value.GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref")
                    .GetString().ShouldBe("#/components/schemas/ApiOperationResponseOf" + schema);
        }
        if (paths.TryGetProperty("/", out var root))
        {
            root.GetProperty("get").TryGetProperty("operationId", out _).ShouldBeFalse();
            root.GetProperty("get").GetProperty("responses").EnumerateObject().Select(p => p.Name).Order().ShouldBe(new[] { "200", "400" });
        }
        var schemas = document.GetProperty("components").GetProperty("schemas");
        schemas.GetProperty("CurrentSessionResponse").GetProperty("properties").TryGetProperty("capabilities", out _).ShouldBeTrue();
        paths.GetProperty("/auth/options").GetProperty("get").GetProperty("operationId").GetString().ShouldBe("GetAuthenticationOptions");
        foreach (var entry in paths.EnumerateObject())
        foreach (var operation in entry.Value.EnumerateObject())
        {
            if (entry.Name.StartsWith("/api/", StringComparison.Ordinal) || entry.Name is "/auth/me" or "/auth/logout")
                operation.Value.GetProperty("security")[0].TryGetProperty("Bearer", out _).ShouldBeTrue();
            else
                operation.Value.TryGetProperty("security", out _).ShouldBeFalse();
        }
        schemas.GetProperty("CustomerOverviewResponse").GetProperty("properties").EnumerateObject().Select(p => p.Name).Order()
            .ShouldBe(new[] { "customerId", "displayName", "billingAvailable", "outstandingBalance", "currency", "hasOutstandingBalance", "customerObservedAt", "billingObservedAt" }.Order());
        foreach (var name in new[] { "CreateCustomerRequest", "ReplaceCustomerRequest", "PatchCustomerRequest" })
            schemas.GetProperty(name).GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
        schemas.GetProperty("PatchCustomerRequest").GetProperty("properties").EnumerateObject().Select(p => p.Name).ShouldBe(new[] { "displayName" });
        paths.TryGetProperty("/health", out _).ShouldBeFalse();
        paths.TryGetProperty("/alive", out _).ShouldBeFalse();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();
        foreach (var route in new[] { "/health", "/alive" })
        {
            var endpoint = endpoints.Single(e => e.RoutePattern.RawText == route);
            endpoint.Metadata.GetMetadata<EndpointSummaryAttribute>()!.Summary.ShouldNotBeNullOrWhiteSpace();
            endpoint.Metadata.GetMetadata<EndpointDescriptionAttribute>()!.Description.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [TestCase("CUSTOMER.INVALID_ID", IssueCategory.Validation, 400)]
    [TestCase("AUTH.UNAUTHENTICATED", IssueCategory.Authorization, 401)]
    [TestCase("AUTH.FORBIDDEN", IssueCategory.Authorization, 403)]
    [TestCase("CUSTOMER.NOT_FOUND", IssueCategory.Business, 404)]
    [TestCase("CUSTOMER.CONFLICT", IssueCategory.Business, 409)]
    [TestCase("BILLING.REJECTED", IssueCategory.Business, 422)]
    [TestCase("APPLICATION.UNEXPECTED", IssueCategory.Technical, 500)]
    [TestCase("CUSTOMER.OUTCOME_UNKNOWN", IssueCategory.Technical, 502)]
    [TestCase("CUSTOMER.AUTHENTICATION_FAILED", IssueCategory.Technical, 502)]
    [TestCase("CUSTOMER.UNAVAILABLE", IssueCategory.Technical, 503)]
    [TestCase("CUSTOMER.CIRCUIT_OPEN", IssueCategory.Technical, 503)]
    [TestCase("CUSTOMER.TIMEOUT", IssueCategory.Technical, 504)]
    public void MapperRetainsFailureStatusesAndNullData(string code, IssueCategory category, int status)
    {
        var issue = new OperationIssue(code, "Safe message", category, "trace");
        var result = OperationResult<NoData>.Failure(issue).ToHttp(_ => new CustomerDeletionResponse("CUST-1"), "trace");
        ((IStatusCodeHttpResult)result).StatusCode.ShouldBe(status);
        var response = ((IValueHttpResult)result).Value.ShouldBeOfType<ApiOperationResponse<CustomerDeletionResponse>>();
        response.Data.ShouldBeNull();
        response.Issues[0].Code.ShouldBe(code);
    }

    [Test] public void SuccessAndWarningRemain200()
    {
        var data = new CustomerDeletionResponse("CUST-1");
        var warning = OperationResult<CustomerDeletionResponse>.Warning(data,
            [new("BILLING.TIMEOUT", "Unavailable", IssueCategory.Technical, "trace")]);
        ((IStatusCodeHttpResult)warning.ToHttp(x => x, "trace")).StatusCode.ShouldBe(200);
        ((IStatusCodeHttpResult)OperationResult<CustomerDeletionResponse>.Success(data).ToHttp(x => x, "trace")).StatusCode.ShouldBe(200);
    }
}
