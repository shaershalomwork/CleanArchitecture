# Dependencies and licensing

Baseline: .NET SDK 10.0.400; exact package versions are centrally pinned in Directory.Packages.props.
Angular uses its lockfile and NSwag 14.6.3. Update manifests and lockfiles together; CI uses npm ci.
The Angular application builder avoids the unused webpack development server. A temporary qs >=6.16 override patches the development-tool dependency chain; remove it when parent packages accept the patched version directly.

| Dependency | Decision |
| --- | --- |
| MediatR 14.2.0 | Retain for top-level dispatch; verify production entitlement for all consuming teams. Configure MediatR:LicenseKey through secrets. |
| AutoMapper | Removed; manual mapping is the default. Versions 15+ have newer licensing requirements. |
| Mapperly | Not installed. Apache-2.0 source generation is an option if mapping repetition warrants an ADR. |
| Dapper 2.1.79 | Apache-2.0; explicit source queries and stored procedures. |
| Microsoft.Data.SqlClient 7.0.2 | Concrete SQL Server provider, confined to Infrastructure. |
| Microsoft.Data.Sqlite 10.0.11 | SQLite provider, confined to Infrastructure; no EF Core dependency. |
| Microsoft.Extensions.Resilience 10.9.0 / Polly | Shared source-operation execution; no blanket HTTP retries. |
| EF Core / local Identity / Respawn | Removed from the default runtime/test design. |
| Aspire 13.5.3 | Optional development diagnostics and disposable source integration tests. |

MediatR 13+ and AutoMapper 15+ have commercial/RPL licensing paths. Do not assume the organization's Community eligibility.
The vendor states that older permissive versions are not supplied with security updates. Do not silently downgrade or suppress licensing logs to bypass review.
Production entitlement is an adoption/release prerequisite; runtime code cannot establish organizational license coverage.
The generated application's MIT notice does not replace dependency terms.

Primary references:
- https://luckypennysoftware.com/faq
- https://docs.automapper.io/en/stable/15.0-Upgrade-Guide.html
- https://github.com/DapperLib/Dapper/blob/main/License.txt
- https://github.com/riok/mapperly
- https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience

Maintain an inventory of direct/transitive licenses and vulnerabilities for each release. Major updates and licensing-sensitive changes require review.
