# Helix Testing Requirements

**Owner:** Helix Core API maintainers  
**Audience:** Engineers, CI maintainers, release managers  
**Last reviewed:** 2026-06-22  
**Status:** Approved  
**Related:** [development-workflow.md](development-workflow.md), [deployment.md](deployment.md), [api-guidelines.md](api-guidelines.md), [database.md](database.md), [authentication.md](authentication.md), [logging-and-monitoring.md](logging-and-monitoring.md)

No Helix service is merged to `develop` or `main` without the automated tests described here. Manual “tested on my machine” is not a substitute for CI.

---

## 1. Tooling and projects

Primary stack: **xUnit**, **FluentAssertions**, **NSubstitute**, **Microsoft.AspNetCore.Mvc.Testing**, **coverlet**, **Testcontainers** (SQL Server image `mcr.microsoft.com/mssql/server:2022-latest`).

`helix-api` test projects:

| Project | Contents |
| --- | --- |
| `Helix.Core.Domain.Tests` | Aggregates, work-order transitions |
| `Helix.Core.Application.Tests` | Handlers, validators |
| `Helix.Core.Infrastructure.Tests` | EF configuration, outbox serializer |
| `Helix.Core.Api.Tests` | HTTP via `WebApplicationFactory` |

Traits: `[Trait("Category", "Unit")]`, `"Integration"`, `"Contract"`, `"Slow"`. CI always runs Unit, Integration, and Contract. `Slow` runs on `helix-api-ci` but is allowed 15 minutes; do not mark ordinary handler tests Slow.

`helix-identity` and `helix-jobs` have analogous `*.Tests` projects. Jobs tests must not require a real Service Bus namespace; use the Azure.Messaging.ServiceBus test doubles or emulator only when documented in the jobs repo.

---

## 2. Unit tests

Unit tests have **no I/O**: no SQL, no HTTP, no Key Vault, no Entra. Domain tests must cover every legal and illegal work-order status transition listed in [api-guidelines.md](api-guidelines.md) (`Draft` → `Scheduled` → `InProgress` → `Completed`, and cancel from `Draft`/`Scheduled`). Completing with `scheduledStartUtc` in the future must fail.

Application tests cover authorization decisions that duplicate the matrix in [authentication.md](authentication.md) (for example Support cannot PATCH work orders). If the matrix changes, tests change in the same PR.

---

## 3. Integration tests

`WebApplicationFactory` hosts `Helix.Core.Api` with:

- Authentication replaced by a test handler that injects roles and `oid` (still validates that middleware **requires** `X-Helix-Tenant-Id` except on `/v1/health` and `/v1/ready`)
- SQL via Testcontainers; database created per test collection, migrations applied with the same `Helix.Migrator` path as CD
- Tenant seed: **Helix QA Tenant** id `33333333-3333-3333-3333-333333333333` plus a second tenant to prove 404 does not leak cross-tenant existence ([api-guidelines.md](api-guidelines.md))

Integration tests must exercise:

- `GET /v1/health` anonymous 200
- `GET /v1/ready` 200 when SQL is up; 503 when SQL is stopped (container stop in a dedicated collection)
- `POST /v1/work-orders` with `Idempotency-Key` replay and conflict
- `PATCH /v1/work-orders/{id}` `If-Match` / `412` / `428`
- Missing tenant header → `400` / `MissingTenantId`
- Wrong tenant membership → `403`

Do **not** run integration tests against `HelixCore_test` on `sql-helix-nonprod` from PR CI (network variance and data pollution). Azure SQL **test** is reserved for Playwright and k6.

Optional env `HELIX_IT_SQL` is only for engineers running integration tests against a private SQL instance; CI ignores it.

---

## 4. Contract tests

Build generates `helix-api.v1.json`. Contract tests fail if:

- A documented public path is removed
- A required request header disappears (`Authorization`, `X-Helix-Tenant-Id` on business routes)
- Error envelope properties `code`, `message`, `correlationId` change
- `pageSize` maximum is not 100

Additive optional JSON fields are allowed. Breaking changes require `/v2` and ADR ([api-guidelines.md](api-guidelines.md)).

---

## 5. Coverage gates

Coverlet in `helix-api-ci`:

| Scope | Line coverage minimum | On failure |
| --- | --- | --- |
| `Helix.Core.Application` | **80%** | CI fails |
| Solution overall (`Helix.Core.*` excluding `Helix.Core.Api` Program wiring) | **70%** | CI fails |

Do not exclude entire handler files to game the gate. Exclude generated code only. Stryker mutation testing on Application is optional and **non-blocking**; if used, results attach to the CI run.

---

## 6. End-to-end and performance

Pipeline **helix-e2e-nightly** runs **02:00 Europe/Stockholm** against **test**:

- Base URL `https://api-test.helix.nordicsystems.internal/v1`
- Entra app **Helix-API-Test** (`aaaaaaaa-0001-4000-8000-000000000003`)
- Synthetic users `qa.operator@nordicsystems.internal` and `qa.reader@nordicsystems.internal` (no production people)
- Tenant `33333333-3333-3333-3333-333333333333`
- Tool: Playwright (repo `helix-e2e`)

Nightly must not use production Front Door or `api.nordicsystems.com`. Failures notify Teams **Helix Platform**, not `ag-helix-p1-prod` ([incident-response.md](incident-response.md)).

Performance: **k6** in Test, pipeline stage on `helix-api-ci` weekly (Friday) and on `release/*` branches:

- Scenario: `GET /v1/work-orders` with operator token
- **50 VUs**, 5 minutes
- Budget: p95 **< 400 ms**
- This budget is stricter than production alert `Helix-WorkOrders-P95` at **800 ms** ([logging-and-monitoring.md](logging-and-monitoring.md))

If k6 fails on a release branch, do not deploy that SHA to staging until fixed or an explicit waiver `HLX-` approved by Platform Engineering.

---

## 7. Security and dependency tests

`helix-api-ci` runs Microsoft Security DevOps and CredScan. **High** or **Critical** on direct package references fails the build ([security.md](security.md)).

Weekly **OWASP ZAP** baseline against Test gateway (pipeline `helix-zap-weekly`, Sunday 03:00 Europe/Stockholm). New High findings open `HLX-` bugs; they do not page on-call. Do not run ZAP against production.

---

## 8. Flaky tests

A test may be skipped with `[Fact(Skip = "HLX-####")]` for at most **3 calendar days**. Skip without a ticket is rejected in review. After 3 days the skip must be lifted or the test deleted with IC/maintainer agreement.

Quarantine is not allowed on contract tests covering auth headers or tenant isolation.

---

## 9. Data rules for tests

- Production data is forbidden in Test, Dev, and CI ([database.md](database.md), [security.md](security.md))
- Do not insert into production QA-looking tenants; the QA GUID exists only in test/CI
- PII in fixtures: clearly fake (`operator.qa@nordicsystems.internal`, names like `QA Contact`)
- Tests that emit Application Insights must use `appi-helix-test` or in-memory; never `appi-helix-prod`

---

## 10. What CD expects before production

[deployment.md](deployment.md) will not start production if linked CI failed. Release managers also require:

1. CI green on the SHA (unit, integration, contract, coverage, security scan)
2. Staging soak 24 hours (or 2-hour hotfix exception)
3. No open P1/P2 on that SHA
4. Nightly e2e green on Test for the last run **unless** the failures are quarantined with tickets and do not include tenant isolation or authentication flows
5. k6 budget met on the release branch

Developers adding a public endpoint without tests covering happy path, 401/403, and tenant 404 will fail Definition of Done ([development-workflow.md](development-workflow.md)).

---

## 11. Running tests locally

```text
dotnet test Helix.sln --filter Category=Unit
dotnet test Helix.sln --filter Category=Integration
```

Docker must be available for Testcontainers. First integration run pulls SQL Server 2022. Do not commit `appsettings` with `Helix-API-Prod` client secrets. Local auth tests use the test auth handler, not live Entra, except a single optional collection `Category=LiveAuth` that is **not** in default CI and requires user secrets for Helix-API-Dev.
