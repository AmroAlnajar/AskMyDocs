# Helix Development Workflow

**Owner:** Helix Core API maintainers  
**Audience:** All engineers contributing to Helix repositories  
**Last reviewed:** 2026-06-20  
**Status:** Approved  
**Related:** [testing.md](testing.md), [deployment.md](deployment.md), [architecture.md](architecture.md), [security.md](security.md), [api-guidelines.md](api-guidelines.md)

Helix source is hosted in Azure Repos under organization **nordic-systems**, project **Helix**. Git is mandatory. This workflow applies to `helix-api`, `helix-identity`, `helix-jobs`, and `helix-infra`.

---

## 1. Branches

| Branch | Purpose | Protected |
| --- | --- | --- |
| `main` | Production-ready history | Yes — no direct push |
| `develop` | Integration for the next release | Yes — no direct push |
| `feature/{ticket}-{short-name}` | Work items | No |
| `bugfix/{ticket}-{short-name}` | Non-hotfix defects | No |
| `release/x.y.z` | Stabilization toward staging/production | Yes after creation |
| `hotfix/{ticket}-{short-name}` | Production repair from `main` | Yes after PR opened |

Examples: `feature/HLX-1842-workorder-priority`, `hotfix/HLX-1901-ready-check-timeout`.

Ticket IDs are Azure Boards work items with prefix **HLX-**. Every PR to `develop` or `main` must link a work item (branch policy).

Lifecycle:

1. Create `feature/*` from current `develop`.
2. Open PR into `develop` (squash merge).
3. Release manager cuts `release/x.y.z` from `develop` when the train starts. Only bugfixes merge into the release branch.
4. After staging soak and production success ([deployment.md](deployment.md)), merge `release/x.y.z` into `main` with a **merge commit** (not squash) and back-merge `main` into `develop`.
5. Hotfix: branch from `main`, PR to `main` (two reviewers), deploy, then merge `main` into `develop`.

Do not use long-lived personal branches as unofficial `develop`. Do not rebase `main` or `develop` (no force-push on protected branches).

---

## 2. Commit messages

Conventional Commits:

```
feat(work-orders): add priority filter to list endpoint
fix(identity): fail closed when membership cache stale on admin routes
docs(api): deprecate legacyTicketNo
test(core): cover InvalidWorkOrderTransition
chore(ci): raise coverlet threshold for Application
refactor(infra): extract outbox serializer
```

`feat` and `fix` must reference `HLX-####` in the PR title or description. Breaking API changes require `BREAKING CHANGE:` footer and an ADR plus OpenAPI major version plan ([api-guidelines.md](api-guidelines.md)).

---

## 3. Pull requests and review

| Target | Minimum approvals | Extra |
| --- | --- | --- |
| `develop` | 1 | Comment resolution required; `helix-api-ci` (or repo CI) green |
| `main` | 2, including a CODEOWNERS owner for touched paths | CI green; work item; no “WIP” title |
| `release/*` | 1 | Same CI |

CODEOWNERS (excerpt):

- `/src/Helix.Identity` and repo `helix-identity` → `@helix-identity-maintainers`
- `/src/Helix.Core.Infrastructure` → `@helix-data-platform`
- `/build/` and `helix-infra` → `@helix-platform-engineering`

The second `main` reviewer must not be the author. Release approvers in Azure Pipelines (`grp-helix-release-approvers`) are **not** a substitute for CODEOWNERS on the Git merge.

Required CI checks (see [testing.md](testing.md) and [security.md](security.md)):

- Unit tests
- Integration tests (Testcontainers SQL)
- Coverage thresholds
- OpenAPI contract test vs `helix-api.v1.json`
- Microsoft Security DevOps: High/Critical fail
- CredScan

PR description must include: intent, tenant impact, migration yes/no, feature flag yes/no, how tested. If the change is HTTP-visible, link the OpenAPI diff.

---

## 4. Versioning and packages

Helix APIs use SemVer. CI stamps assemblies with MinVer using tags `vX.Y.Z` on `main`. Container tags use Git SHA plus `release-{semver}` ([deployment.md](deployment.md)).

Shared libraries publish to Azure Artifacts feed **helix-internal** as `NordicSystems.Helix.*`. Consuming a new BuildingBlocks version (`NordicSystems.Helix.BuildingBlocks` 4.3.x baseline per [architecture.md](architecture.md)) requires a dedicated PR, not a drive-by bump inside a feature PR unless the feature needs it.

---

## 5. Local development

Supported: Visual Studio 2026 or VS Code with the .NET 10 SDK. Solution `Helix.sln` in `helix-api`.

Database: SQL Server LocalDB, database name **`HelixCore`**. Apply migrations with `Helix.Migrator` or `dotnet ef database update` against LocalDB only. Optional Docker Compose in the repo runs `helix-api` on port 8080; it must not include credentials for Azure SQL.

Secrets: **.NET user secrets**. Never put real connection strings in `appsettings.Development.json`. Allowed local overrides: log level Debug, `AzureAd` using **Helix-API-Dev** client id `aaaaaaaa-0001-4000-8000-000000000004`, tenant `11111111-aaaa-bbbb-cccc-222222222222`. Do not use Helix-API-Prod registration locally ([authentication.md](authentication.md)).

Pointing local apps at `sql-helix-prod` or `kv-helix-prod` is forbidden ([security.md](security.md)). Engineers may read **dev** Key Vault `kv-helix-dev` after Azure login for debugging telemetry.

Synthetic tenant for manual UI: not the QA tenant. QA tenant `33333333-3333-3333-3333-333333333333` is reserved for **test** Azure SQL ([database.md](database.md), [testing.md](testing.md)).

---

## 6. Definition of Done

A work item is Done only when:

1. Squash-merged to `develop` (or hotfix merged to `main` per process)
2. Tests required by [testing.md](testing.md) are added or updated (unit for domain/application; integration if HTTP or SQL contract changes)
3. OpenAPI updated if public endpoints changed
4. Observability: new failure modes have logs with `CorrelationId` and, if user-facing, a documented error code
5. Security: no new secrets in Git; Key Vault names follow `--` hierarchy if new secrets are required
6. If schema changed: EF migration reviewed by `@helix-data-platform`, expand/contract compatible with slot swap
7. Feature flags in `core.TenantFeatures` default off in production until explicitly enabled per tenant
8. Docs in this KnowledgeBase updated when behavior of environments, auth, or deploy changes (same PR or follow-up `HLX-` linked before release branch cut)

Done on `develop` does **not** mean production. Production requires CD, two pipeline approvers, and staging soak ([deployment.md](deployment.md)).

---

## 7. Release train (engineering view)

Typical cadence: cut `release/x.y.z` **Monday**, deploy staging **Monday afternoon**, soak 24 hours, production slot swap **Tuesday–Thursday 07:00–09:00 Europe/Stockholm**.

Engineers do not queue production CD unless they are on the release rota or fixing a hotfix. After production, post SHA to Teams **Helix Platform**.

If nightly Playwright (`helix-e2e-nightly`, 02:00 Europe/Stockholm) fails on **test**, the release manager may block cutting a release until Test is green, unless the failure is classified flaky under [testing.md](testing.md) quarantine rules.

---

## 8. CODEOWNERS vs incidents

Incident hotfixes still use `hotfix/*` PRs. Incident Commander may approve an emergency merge with a single available CODEOWNER plus IC comment on the PR when the second reviewer is unavailable; this must be recorded on the `INC-*` work item ([incident-response.md](incident-response.md)). Skipping CI is never allowed.
