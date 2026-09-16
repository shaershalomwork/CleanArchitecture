# Customer workspace and local authentication

The Angular workspace covers customer listing, overview, and writes. The backend remains the authority for every operation. The fake registry starts empty; create customers explicitly before browsing. There is no user database, administrator role, role-to-permission mapping, or billing-write API in this baseline.

## Local Bearer tokens

Run from the solution directory:

```powershell
dotnet user-jwts create --project src/Web --name dev-reader --claim permissions=customers.read --valid-for 1h
dotnet user-jwts create --project src/Web --name dev-writer --claim permissions=customers.write --valid-for 1h
dotnet run --project src/Web --launch-profile https
```

The CLI writes issuer/audience configuration into the Web project's appsettings.Development.json and stores the signing key in its User Secrets under Authentication:Schemes:Bearer:SigningKeys. Never copy the signing key into source control. The existing Web UserSecretsId is used; generated applications receive distinct IDs. Run the CLI against the Web project, not AppHost. Restart the application after creating or rotating credentials to ensure the current configuration is loaded.

Open `/scalar`, select Bearer authentication, and paste the desired token. Protected operations send it automatically. Routine local use needs no authentication-mode changes, authority configuration, or Scalar configuration edits. Tokens with only customers.write cannot read the overview. `--role` and `--scope` do not grant customer permissions. The CLI accepts one value per custom claim name; do not repeat the permissions claim argument to create a combined token. Use the reader/writer browser profile when exercising combined access locally.

Local tokens work only in Development authentication mode, which startup permits in Development/Test environments. External mode retains authority-based validation and rejects local signing keys even when local scheme settings are present. With no local key configured, browser demo login still works and Bearer authentication fails closed. Validation checks signature, issuer, audience, expiry, and not-before time.

When a request supplies Bearer credentials, those credentials determine its identity. Invalid credentials return 401 even alongside a valid browser cookie. Claims from the cookie and Bearer token are never merged. Authenticated mutations require antiforgery unless the successful authentication ticket establishing the current principal came from the Bearer handler. Merely adding an Authorization header never exempts a cookie request.

## Access profiles

| Profile | Customer permissions | Workspace access |
|---|---|---|
| Reader (default) | customers.read | List, lookup, and overview |
| Writer | customers.write | Create, rename, full edit, and delete by ID |
| Reader/writer | Both | All customer workflows |
| No access | Neither | Account and service status |

Development sign-in offers these four fixed profiles. Direct demo login accepts `/auth/login?profile=reader-writer&returnUrl=/`; omitting profile selects Reader. Unknown profiles are rejected. External mode exposes no demo profiles and rejects a profile argument. Corporate roles are displayed on Account, but no role mapping is inferred. Consuming teams must explicitly define any organizational role mapping on the backend before using it.

Angular derives navigation and actions from policy-evaluated capabilities returned by `/auth/me`. A null display name does not mean the user is anonymous. Route guards and hidden controls improve navigation; the server independently applies CustomerOverview.Read and Customer.Write to every customer request.

## API and workflow inventory

| Server operation | Workspace workflow |
|---|---|
| GET /api/customers | `/customers`; all registered IDs and display names, refresh, and exact-ID lookup |
| GET /api/customers/{customerId}/overview | `/customers/:customerId/overview`; registered profile, billing, source observations, partial-data warnings |
| POST /api/customers | `/customers/create`; immutable caller-supplied ID, name, success receipt |
| PUT /api/customers/{customerId} | `/customers/manage`, Edit details; submit all editable fields |
| PATCH /api/customers/{customerId} | `/customers/manage`, Change name; partial JSON object |
| DELETE /api/customers/{customerId} | `/customers/manage`, Delete; confirmation and receipt |
| GET /auth/login | `/sign-in`; OIDC browser flow or local profile |
| POST /auth/logout | Header Sign out; browser form preserves OIDC redirects |
| GET /auth/me | Session initialization and `/account` |
| GET /auth/options | Sign-in availability and fixed development profile IDs/labels |
| GET /auth/antiforgery | Internal token acquisition for browser mutations |
| GET /alive | `/status`, application availability |
| GET /health | `/status`, aggregate source readiness; recognizes Degraded as well as Healthy/Unhealthy |
| /openapi/v1.json and /scalar | API reference links and interactive Bearer requests |
| / and Angular fallback | Permission-aware landing route and client navigation |
| OIDC callbacks | External sign-in, remote sign-out, and sign-out return workflow; no standalone screens |

Authentication responses remain native JSON rather than customer envelopes. `/auth/me` adds id, roles, permissions, and capabilities while retaining nullable name. `/auth/options` exposes only loginAvailable and developmentProfiles. Session responses are not cacheable. Antiforgery tokens are held in memory and attached to same-origin mutations in X-CSRF-TOKEN; the paired cookie is retained automatically. Sign-out uses the same protection through the existing form field. Customer responses retain status/data/issues/correlationId and existing HTTP codes.

Writer-only workflows never fetch a protected overview, including after a successful write. Changes use last-write-wins semantics. Billing is not modified by customer writes. There are no new source tables, migrations, search endpoints, or idempotency guarantees.

## Appearance and failure handling

Light, Dark, and System themes are available in the header and Account. System follows device changes; explicit selections persist under the existing picoColorScheme browser key. Only this appearance preference is persisted. Customers, tokens, and session claims are not stored in localStorage. Storage failure falls back to an in-memory preference. The initial page resolves the theme before Angular renders.

Forms retain input after failure and show validation, conflict, missing-customer, forbidden, loading, success, and error states. Warnings preserve available profile data when billing is unavailable. Writes are never automatically retried. CUSTOMER.OUTCOME_UNKNOWN or a lost write response requires reconciliation; readers can explicitly check the overview, while writer-only users must ask an authorized operator to check the system of record. Reconcile against the source before another attempt; an overview can itself be unavailable or degraded.

## Verification

Backend functional tests exercise the real Integration selector, mixed credentials, local and External validation, fixed profiles, CSRF on cookies, and policy enforcement. Angular tests cover theme resolution, session capabilities, forms, and antiforgery transport. Playwright covers all four profiles, every write operation, warning/error states, mobile layouts, and theme persistence; screenshots are saved as test artifacts.

The template verifier creates short-lived CLI tokens for an isolated generated project before publishing, tests Bearer reads/writes, then clears the issued tokens and signing key. Its six frontend/provider combinations also verify clean npm installs, generated clients, publishing, and browser workflows. On Windows with Edge, set CHROME_BIN to its executable and PLAYWRIGHT_BROWSER_CHANNEL to msedge. External identity-provider verification requires deployment credentials and is not simulated as a corporate connectivity test.
