# ADR-004: Data-centric integration baseline

Status: Accepted. Supersedes ADR-001 and ADR-003; amends ADR-002.

## Decision

External systems own their records. Application owns orchestration, validation, aggregation and explicit degradation decisions.
Infrastructure implements Application-owned capability interfaces and owns SQL, stored procedures, external payloads, connection lifetimes and execution policies.
Web owns authentication, authorization, binding and HTTP status translation.
Domain remains empty until an application owns real entities or invariants. No external records become pseudo-aggregates.

Use manual mapping and OperationResult<T> throughout use cases and adapters. Success has data and no issues; Warning has data and issues; Error has issues without usable data.
Issues have stable codes, safe messages, categories and correlation IDs. The first error issue determines the primary HTTP failure.
Caller cancellation propagates. Expected source failures are categorized; unexpected faults are converted at the use-case/HTTP boundary.

MediatR is retained only for top-level dispatch and result-aware logging/metrics, error handling and validation behaviors.
Source-dependent validation is workflow logic, not a validator performing hidden I/O.

SQL uses Dapper and named SqlClient connection factories. HTTP uses IHttpClientFactory. Shared Polly pipelines have explicit read-only operation metadata.
Unknown operations and writes never retry automatically. Write timeouts/disconnects report OUTCOME_UNKNOWN and require reconciliation.
No distributed transactions, generic repositories, visual orchestrators, or workflow engines are introduced.

## Reference feature

Customer overview first reads the required registered customer, then requests its optional HTTP billing summary. Missing or failed registry reads stop before billing. Reads never create customer records.
The customer list reads the same registry through the Application-owned read interface and returns all registered profiles without billing. Development fake state starts empty; tests explicitly register fixtures. See [customer reads](../customer-reads.md).
Only billing TIMEOUT, UNAVAILABLE and CIRCUIT_OPEN permit a Warning. Missing billing records, bad credentials or malformed payloads terminate the use case.
Unavailable balances remain null, and payment attention remains unknown. A successfully returned zero is legitimate data.
Source observation times do not imply an atomic cross-system snapshot.

## Consequences

More explicit models and mappings improve reviewability at the cost of boilerplate.
Angular and API-only are supported; React was removed at the user's request.
Aspire remains optional for development and supports disposable database tests. Core functional tests use fakes and do not start Aspire.
Local Identity, sample persistence, schema deletion/creation, domain events and AutoMapper are removed.
Future owned persistence is a separate opt-in design and must never migrate an external system's schema.
