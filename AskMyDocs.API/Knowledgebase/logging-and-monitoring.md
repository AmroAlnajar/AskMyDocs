# Helix Logging and Monitoring

**Owner:** Helix Platform Operations  
**Audience:** Developers and on-call  
**Last reviewed:** 2026-06-25  
**Status:** Approved  
**Related:** [architecture.md](architecture.md), [api-guidelines.md](api-guidelines.md), [incident-response.md](incident-response.md), [deployment.md](deployment.md), [security.md](security.md)

All Helix services log through **Serilog** to console (JSON) and **Azure Application Insights**. Operators search logs in Application Insights and the linked Log Analytics workspace, never on App Service local files.

---

## 1. Resources per environment

| Environment | Application Insights | Log Analytics | Dashboard |
| --- | --- | --- | --- |
| Development | `appi-helix-dev` | `log-helix-dev` | Helix Dev Overview |
| Test | `appi-helix-test` | `log-helix-test` | Helix Test Overview |
| Staging | `appi-helix-stage` | `log-helix-stage` | Helix Staging Overview |
| Production | `appi-helix-prod` | `log-helix-prod` | **Helix Production Overview** |

Resource groups match [architecture.md](architecture.md) (`rg-helix-{env}`). Instrumentation connection strings are Key Vault secret `AppInsights--ConnectionString` (see [security.md](security.md)). Do not embed instrumentation keys in source.

Retention: **30 days** in non-production workspaces, **90 days** in `log-helix-prod`. Security incidents may place a legal hold (365 days) as described in [incident-response.md](incident-response.md).

---

## 2. Correlation and required log properties

The canonical request id is header **`X-Helix-Correlation-Id`** (UUIDv4). Gateway generates it if missing and returns it on every response ([api-guidelines.md](api-guidelines.md)). Serilog enrichers copy it to property `CorrelationId`. `Activity.TraceId` is also recorded as `TraceId` for W3C distributed tracing (`traceparent` is accepted and forwarded).

Every log event from `helix-core-api`, `helix-gateway`, `helix-identity`, and `helix-jobs` must include:

| Property | Source |
| --- | --- |
| `ServiceName` | `Helix__ServiceName` (`helix-core-api`, `helix-gateway`, `helix-identity`, `helix-jobs`) |
| `Environment` | `ASPNETCORE_ENVIRONMENT` |
| `CorrelationId` | Header or generated |
| `TenantId` | `X-Helix-Tenant-Id` when present |
| `UserObjectId` | Token `oid` when authenticated |
| `ClientId` | Token `azp` or `appid` for daemon callers |

When a work order is in play, include `WorkOrderId`. When SQL is called, Application Insights dependency telemetry uses `Application Name` from the connection string ([database.md](database.md)).

Do not log access tokens, refresh tokens, connection strings, Key Vault secret values, or `PrimaryContactEmail` in Information logs. Email may appear in HelixAudit via the dedicated audit ingest path, not in AppTraces. Logging PII in traces is a security defect.

---

## 3. Log levels by environment

| Environment | Default minimum level | Notes |
| --- | --- | --- |
| Development | Debug | Console pretty-print allowed locally; Azure still JSON |
| Test | Information | Debug allowed for a named logger `Helix.Core.Infrastructure.Sql` for 24h via App Config flag `Helix:Logging:SqlDebug` |
| Staging | Information | Same as prod sampling policy but no paging |
| Production | Information | Adaptive sampling enabled |

Production host.json for Functions (`helix-jobs`): default **Warning** for `Function` category, **Information** for `Helix.*` namespaces so business outbox logs remain visible.

Serilog overrides: `Microsoft.AspNetCore` Warning, `Azure.Core` Warning, `Helix.Core.Application` Information.

---

## 4. Sampling and health endpoints

Application Insights adaptive sampling is **on** in staging and production. Exclusions:

- Failed requests (4xx/5xx) are never sampled away
- Exceptions are never sampled away
- `GET /v1/health` **successful** requests are excluded from telemetry to reduce noise (failed health remains)
- `GET /v1/ready` is kept (low volume)

Custom events (use `TelemetryClient.TrackEvent`):

- `WorkOrderCreated`
- `WorkOrderCompleted`
- `TenantProvisioned`
- `IdempotencyReplay`

Event names are stable; dashboards depend on them. Do not rename without updating **Helix Production Overview**.

---

## 5. What to query (Kusto)

Workspace tables: `AppTraces`, `AppRequests`, `AppExceptions`, `AppDependencies`, `AppCustomEvents`.

Find a request by correlation id (incident callers will give you this from the error envelope):

```kusto
AppRequests
| where TimeGenerated > ago(24h)
| where tostring(customDimensions.CorrelationId) == "<guid>"
| project TimeGenerated, name, resultCode, duration, cloud_RoleName
```

Also search `AppTraces` with the same `CorrelationId` filter. `cloud_RoleName` must be `helix-core-api`, `helix-gateway`, `helix-identity`, or `helix-jobs` (not the default `azurewebsites.net` site name).

---

## 6. Alerts and action groups

Alerts are defined in Bicep (`helix-infra`) and must not be created only in the portal.

| Alert name | Condition | Severity | Action group |
| --- | --- | --- | --- |
| `Helix-Availability-Drop` | Availability < 99% over 5 minutes (availability test + request success) | P1 | `ag-helix-p1-prod` |
| `Helix-5xx-Rate` | Server 5xx rate > **5%** for 5 minutes on `helix-gateway` or `helix-core-api` | P1 | `ag-helix-p1-prod` |
| `Helix-Auth-401-Spike` | 401 count > 200 in 5 minutes **and** 401 ratio > 20% of requests | P2 | `ag-helix-p2-prod` |
| `Helix-WorkOrders-P95` | p95 duration for `GET /v1/work-orders` > **800 ms** for 15 minutes | P2 | `ag-helix-p2-prod` |
| `Helix-Sql-Dependency-Failures` | SQL dependency failures > **2%** for 10 minutes | P2 | `ag-helix-p2-prod` |
| `Helix-Ready-Fail` | `/v1/ready` failures from App Service health > 3 consecutive | P2 | `ag-helix-p2-prod` |
| `Helix-Jobs-Outbox-Lag` | Oldest unprocessed `ops.OutboxMessages` > 15 minutes (metric from function `OutboxLagProbe`) | P2 | `ag-helix-p2-prod` |
| `Helix-FrontDoor-WAF-Blocked` | WAF blocked requests > 1,000 in 10 minutes | P3 | Teams Helix Platform only |

Non-production alerts notify Teams **Helix Platform** only; they do not SMS. Production P1 action group `ag-helix-p1-prod` notifies SMS + phone + Teams **Helix Incidents**. P2 uses `ag-helix-p2-prod` (Teams + SMS, no voice).

Availability tests: ping **`https://api.nordicsystems.com/helix/v1/health`** from Sweden Central and West Europe every 5 minutes, and ping internal `https://api.helix.nordicsystems.internal/v1/health` from a VNet-connected test (`func-helix-jobs-prod` timer `InternalHealthProbe`). Public health does not require `X-Helix-Tenant-Id`.

p95 budget used by performance tests in **test** is stricter (400 ms at 50 VUs for GET work-orders, [testing.md](testing.md)). The **800 ms** production alert is an operational ceiling, not the engineering SLO target. Engineering SLO for production GET work-orders is p95 **400 ms** measured weekly; breach is a P3 backlog item unless it also trips the 800 ms alert.

---

## 7. On-call use of dashboards

Start with dashboard **Helix Production Overview** (Azure Portal, Helix dashboards subscription prod):

1. Gateway 5xx and 429
2. Core API request duration heat map
3. SQL dependency duration and failure
4. Service Bus queue depth `helix.workorder.changed` and `helix.notify.email`
5. Function execution failures

Queue depth growing while API is healthy usually means `helix-jobs` or Service Bus throttling, not a gateway issue. Identity 503s on writes with code `IdentityUnavailable` point at `helix-identity` or NSG/private endpoint, not at Entra (Entra failures more often present as 401 spikes).

During a deploy ([deployment.md](deployment.md)), watch the same dashboard for 30 minutes after slot swap. A swap-back is the correct first mitigation if 5xx exceeds 5% for five minutes on the new slot traffic.

---

## 8. Local development

Local Serilog: console (and Seq if `SEQ_URL` is in user secrets). Never use `appi-helix-prod` from a workstation. `appi-helix-dev` is allowed via `kv-helix-dev` after Azure CLI login — do not commit the connection string.
