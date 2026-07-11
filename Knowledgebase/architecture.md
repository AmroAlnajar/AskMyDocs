# Helix Platform Architecture

**Owner:** Platform Architecture Board  
**Audience:** Helix engineers and on-call  
**Last reviewed:** 2026-06-12  
**Status:** Approved  
**Related:** [authentication.md](authentication.md), [api-guidelines.md](api-guidelines.md), [database.md](database.md), [deployment.md](deployment.md), [security.md](security.md)

Helix is Nordic Systems’ internal multi-tenant platform for industrial service contracts, customer accounts, assets, and work orders. Field-service, sales, and partner-integration teams depend on it.

---

## 1. Scope and context

Helix runs as a set of ASP.NET Core Web APIs and Azure Functions on Microsoft Azure, primarily in **Sweden Central**. Production disaster recovery for Azure SQL is configured to **West Europe**. All services target **.NET 10**. Public partner traffic enters through Azure Front Door (`afd-helix-prod`); internal traffic uses the private DNS zone `helix.nordicsystems.internal`.

The platform must support approximately 180 tenants (Nordic Systems business units and selected partners). Each request is scoped by `X-Helix-Tenant-Id`. Tenant isolation is logical (row-level `TenantId` plus application filters), not physical databases per tenant, except for the dedicated audit database described in [database.md](database.md).

---

## 2. Service catalog

| Service name | Runtime | Hosting | Azure resource (pattern) | Responsibility |
| --- | --- | --- | --- | --- |
| `helix-gateway` | ASP.NET Core | App Service Linux | `app-helix-gateway-{env}` | Edge routing, rate limiting, correlation-id generation if missing |
| `helix-identity` | ASP.NET Core | App Service Linux | `app-helix-identity-{env}` | Token validation helpers, service-to-service client registry, user-tenant membership cache |
| `helix-core-api` | ASP.NET Core | App Service Linux | `app-helix-api-{env}` | Customers, contracts, assets, work orders |
| `helix-jobs` | Azure Functions isolated worker | Functions Premium | `func-helix-jobs-{env}` | Outbox dispatch, email/SMS notify, nightly aggregations |

App Service plans are `asp-helix-{env}` (Linux). Non-production uses **P1v3** (one instance). Production uses **P2v3** with a minimum of **three** instances for `helix-core-api` and `helix-gateway`, and **two** instances for `helix-identity`. Functions use plan `ep-helix-jobs-{env}` (Elastic Premium EP1 in non-prod, EP2 in prod).

Internal base URLs:

- Production: `https://api.helix.nordicsystems.internal/v1`
- Staging: `https://api-stage.helix.nordicsystems.internal/v1`
- Test: `https://api-test.helix.nordicsystems.internal/v1`
- Development: `https://api-dev.helix.nordicsystems.internal/v1`

`helix-gateway` is the only service that binds to these hostnames. It reverse-proxies `/v1/*` business routes to `helix-core-api` and `/v1/identity/*` to `helix-identity`. Direct calls to `app-helix-api-*` from developer workstations are allowed in **dev** and **test** only, and are blocked by NSG in staging and production (see [security.md](security.md)).

Partner-facing production URL: `https://api.nordicsystems.com/helix/v1` (Front Door + WAF, Prevention mode, OWASP 3.2).

---

## 3. Solution structure

The primary repo is Azure Repos `helix-api` (org `nordic-systems`, project **Helix**). `Helix.sln`: `Helix.Core.Api`, `Helix.Core.Application`, `Helix.Core.Domain` (`Customer`, `WorkOrder`, `Asset`, `Contract`), `Helix.Core.Infrastructure`, `Helix.BuildingBlocks`. Related repos: `helix-identity`, `helix-jobs`, `helix-infra` (Bicep). Packages: Azure Artifacts **helix-internal**, prefix `NordicSystems.Helix.*`, baseline `NordicSystems.Helix.BuildingBlocks` **4.3.x**.

---

## 4. Request flow

1. Client (Helix Portal, partner integration, or `helix-jobs`) sends HTTPS to the gateway with `Authorization: Bearer` and `X-Helix-Tenant-Id`.
2. Gateway writes or forwards `X-Helix-Correlation-Id` (UUIDv4). If the header is absent, gateway generates one. This value is the canonical correlation id for logs, incidents, and error bodies (see [logging-and-monitoring.md](logging-and-monitoring.md) and [api-guidelines.md](api-guidelines.md)).
3. Gateway enforces rate limits (600 requests/minute per user object id; 3,000/minute per `Helix.Integration` client id) and rejects missing tenant headers with `400` / `MissingTenantId`.
4. Identity middleware on `helix-core-api` validates the JWT against Entra ID tenant `11111111-aaaa-bbbb-cccc-222222222222`, audience `api://helix-api`. Role mapping is described in [authentication.md](authentication.md).
5. Application handlers load tenant-scoped aggregates from Azure SQL database **HelixCore**.
6. State-changing handlers write a row to the transactional outbox table `ops.OutboxMessages` in the same SQL transaction as the business write.
7. `helix-jobs` function `OutboxDispatcher` (timer every 10 seconds, plus Service Bus trigger on `helix.workorder.changed`) publishes to Service Bus namespace `sb-helix-{env}.servicebus.windows.net`.

Synchronous HTTP calls between `helix-core-api` and `helix-identity` are allowed only for membership cache miss (`GET` internal endpoint `/internal/tenants/{tenantId}/members/{objectId}`). This internal path is not exposed on the gateway. Timeout is 2 seconds; on failure the API returns `503` / `IdentityUnavailable` and does not fail open for write operations.

---

## 5. Data stores and messaging

- **HelixCore** — system of record for customers, contracts, assets, work orders.
- **HelixIdentity** — client registry, tenant membership, API client secrets *references* (secret material lives in Key Vault, not SQL).
- **HelixAudit** — append-only audit of PII access and administrative actions.

SQL servers: `sql-helix-prod.database.windows.net` (production subscription `sub-nordic-helix-prod`) and `sql-helix-nonprod.database.windows.net` (non-production subscription `sub-nordic-helix-nonprod`). Connection strings are never stored in App Settings as plaintext; production and staging use Key Vault references as specified in [security.md](security.md) and [deployment.md](deployment.md).

Service Bus queues (all environments, same names):

- `helix.workorder.changed`
- `helix.notify.email`
- `helix.audit.ingest`

Topic: `helix.tenant.events` (subscriptions `identity-sync` and `jobs-provision`).

---

## 6. Configuration and identity

Each service uses a **user-assigned managed identity**:

- `id-helix-gateway-{env}`
- `id-helix-api-{env}`
- `id-helix-identity-{env}`
- `id-helix-jobs-{env}`

These identities are granted `Get` and `List` on vault `kv-helix-{env}` and `SQL DB Contributor` equivalent Azure AD database users in HelixCore / HelixIdentity / HelixAudit (see [database.md](database.md)). App configuration uses the Key Vault secret naming convention with `--` hierarchy, for example `Sql--HelixCore--ConnectionString`.

Local development uses .NET user secrets and SQL Server LocalDB database name `HelixCore`. Local development must not point at `sql-helix-prod`.

---

## 7. Architecture decisions (current)

| ID | Decision | Notes |
| --- | --- | --- |
| ADR-014 | Single HelixCore database, shared schema, `TenantId` column | Physical isolation rejected due to 180-tenant ops cost |
| ADR-017 | Gateway owns public and internal hostnames | Core API has no public ingress in stage/prod |
| ADR-021 | Transactional outbox, not dual-write to Service Bus | Functions are the only Service Bus publishers for domain events |
| ADR-022 | Entra ID as the only identity provider | No local password store in Helix |
| ADR-025 | Sweden Central primary, West Europe SQL geo-replica in prod | App Services are not multi-region active-active |
| ADR-028 | .NET 10 isolated Functions worker | In-process model is not used |

New independently deployed services need an ADR. Prefer new modules in `helix-core-api` unless scale needs match `helix-jobs`.

---

## 8. Environments

| Environment | Resource group | Subscription alias | Auto-scale | Data |
| --- | --- | --- | --- | --- |
| Development | `rg-helix-dev` | `sub-nordic-helix-nonprod` | Off (1 instance) | Synthetic |
| Test | `rg-helix-test` | `sub-nordic-helix-nonprod` | Off | Synthetic; QA tenant `33333333-3333-3333-3333-333333333333` |
| Staging | `rg-helix-stage` | `sub-nordic-helix-nonprod` | 2 instances gateway/api | Anonymized subset restored monthly |
| Production | `rg-helix-prod` | `sub-nordic-helix-prod` | Min 3 api/gateway | Live tenant data |

Production deploys use App Service slot `slot-preprod` and swap only after the staging environment soak described in [deployment.md](deployment.md). Architecture reviews assume that **test** is the environment automated tests may call; **staging** is production-like and is not a dump for ad-hoc experiments.

---

## 9. Helix Portal and clients

Helix Portal (`https://portal.helix.nordicsystems.internal`, staging `https://portal-stage.helix.nordicsystems.internal`) uses **Helix-Portal-Prod** (and env siblings) with PKCE. CORS is in [security.md](security.md). Field apps use audience `api://helix-api` and `X-Helix-Tenant-Id`.
