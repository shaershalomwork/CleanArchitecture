# Integration guide

## Add a capability

1. Define the narrow interface and immutable model in Application/<Feature>/Sources.
2. Implement it in Infrastructure/Sources/<Source>. Keep external DTOs private to that adapter.
3. Use SourceExecutor with constant source/operation names. Set IsReadOnly only after confirming that the operation has no side effects, including stored procedures.
4. Allocate connections and HTTP messages inside the execution delegate so each retry owns fresh resources.
5. Pass cancellation through every asynchronous operation and explicitly map business return codes.
6. Add a deterministic fake, contract tests, and the workflow's explicit degradation policy.

Do not return null, an empty list or zero after a failed call. Do not forward upstream exception messages to users.
Do not retry a write after OUTCOME_UNKNOWN until its status is reconciled with the system of record.
Keep any transaction inside one adapter and one database. Multi-system writes need an explicit business recovery design.

## Reference source contracts

CustomerRegistry uses ConnectionStrings:<ConnectionName>, defaulting to CustomerRegistry.
dbo.GetCustomer accepts @CustomerId nvarchar(50) and returns one row with Id and DisplayName.
Return code 0 means a valid row; 404 means absent with no row. Other codes, missing fields, or mismatched IDs are invalid responses.
The schema in tests/Infrastructure.IntegrationTests/Fixtures is only for the disposable fixture; the application never executes it.

Billing uses an HTTPS BaseUrl ending in / and an ApiKey sent as X-Api-Key.
GET customers/{escapedCustomerId}/summary returns { "outstandingBalance": 125.50, "currency": "USD" }.
Both properties are required; currency is three uppercase letters. Configure the source adapter to its actual organizational contract before adoption.
GET health is its optional readiness probe. The reference authentication protocol is illustrative; implement a source-specific credential provider for OAuth or legacy service authentication.

The connection factory supports multiple named connections. SqlClient owns pooling; every attempt leases and disposes a connection.
SQL session context carries CorrelationId and is cleared on disposal. A failed cleanup discards the pool rather than reusing contaminated session state.

## Configuration

Base appsettings.json selects Live and External. appsettings.Development.json selects Fake and Development.
Per-source options allow independent mode selection in Development/Test. Non-development startup rejects all fake modes.
Live settings validate at startup without probing corporate sources. Readiness probes are separate from configuration validation.

Use user secrets or environment variables during development and a secret provider in deployments.
AZURE_KEY_VAULT_ENDPOINT enables the existing Key Vault configuration provider. Never put connection passwords, API keys or MediatR keys into committed JSON.
Example environment names: ConnectionStrings__CustomerRegistry, Sources__Billing__BaseUrl, Sources__Billing__ApiKey, Authentication__Authority, Authentication__Audience.
For SqlClient 7 Entra authentication, add its Azure authentication extension deliberately when adopting that authentication mode.

Read defaults: 3-second attempts, 8-second total budget, one retry with exponential jitter starting at 200ms.
Circuit breakers are isolated by source/operation: 50% failure ratio, ten attempts per 30-second sample, 15-second break.
SourceExecution settings validate numeric ranges and budget relationships. Tune against source SLAs.
The Web request budget is 15 seconds. Avoid stacking provider/client retry policies with SourceExecutor.

## Authentication

API-only uses externally issued JWTs with signature, issuer, audience and lifetime validation.
CustomerOverview.Read requires the permissions claim customers.read.
Angular uses server-side OIDC code flow with PKCE and secure cookies; configure ClientId and ClientSecret and register /signin-oidc and /signout-callback-oidc.
Use HTTPS in deployments and the HTTPS launch profile locally. Persist/protect ASP.NET Core Data Protection keys using the organization's deployment platform for multiple instances.
Development sign-in is only registered behavior in Development/Test mode and represents a fixed demo reader.
Cookie-authenticated mutations require antiforgery tokens; /auth/antiforgery supplies one. Logout uses a browser form so OIDC redirects work.

The API returns 401/403 rather than login redirects. Angular and the API share an origin; any additional CORS origins must be explicitly configured.
Downstream calls use source credentials, not automatically forwarded caller access tokens.

## Results and observability

Web serializes ApiOperationResponse<T>, independently mapped from Application models.
200 can mean Success or Warning. Inspect status, issues and billingAvailable, not only the HTTP status.
Errors have data:null. Stable issue codes map to HTTP only in Web; a requested customer missing is 404, but an upstream credential failure is 502.
Every response includes X-Correlation-ID. Issues carry the same trace ID; HTTP downstream requests propagate W3C tracing and X-Correlation-ID.
Sources emit duration, attempt, result and circuit-transition metrics under CleanArchitecture.Integrations. Metric labels never include customer identifiers.
Optional source failures degrade readiness without killing liveness. Restrict probe routes through deployment ingress; responses expose no credentials or source exception details.

Add gRPC/webhook entry points as Web adapters around the same use cases when required. File exports and message destinations use narrow output capabilities with source-specific handling in Infrastructure.
