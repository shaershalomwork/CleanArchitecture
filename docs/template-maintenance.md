# Template maintenance and rollout

The organizational package is DataCentric.Integration.Solution.Template, with di-sln and di-usecase short names.
It is distinct from upstream and can be installed alongside it. Generated projects record the package version and upstream commit.

## Versioning

Major: generated public contract or CLI breaks. Minor: additive features. Patch: compatible fixes.
Support the current and previous organizational major for twelve months after a new major.
The maintainer reviews upstream monthly and selectively ports relevant fixes.
Generated projects do not update themselves: supply migration notes and targeted patches to consuming teams.
Each consumer owns its actual source contracts, credentials, data access permissions and business degradation rules.

## Release gates

1. Build and test the source with the pinned SDK.
2. Run build/test.ps1 -BrowserTests against the actual package, covering Angular and API-only, renamed namespaces, nested generated use cases, publish and browser tests.
3. Run SourceIntegration against disposable SQL Server. A skipped fixture is not a passed source-contract check.
4. Review package contents and license notices, package inventory, vulnerabilities, and production MediatR entitlement.
5. Publish a candidate to the configured organizational feed, then run one pilot against its real IdP and sources.
6. Review source permissions, schema contracts, budgets, warning rates, and trace correlation. Promote the same tested artifact only after the pilot.

Release automation requires an explicitly configured TEMPLATE_FEED_URL repository variable and TEMPLATE_FEED_API_KEY secret.
It does not default to publishing under upstream's identity or to NuGet.org.
Publication is gated through the template-release environment. Configure the organization's review/pilot requirements there.
Rollback a template release by selecting the previous package; application rollout/rollback remains owned by each consumer.

## Verification notes

Core functional tests replace sources and authentication in-process and require no corporate network.
SourceIntegration creates its own database through TestAppHost, initializes only that disposable connection, and never accepts a user-supplied corporate connection string.
Browser tests consume a published Development application through TEST_BASE_URL.
The source audit was performed on 705d77f before a compatible SDK was available; no successful pre-migration build is claimed.
