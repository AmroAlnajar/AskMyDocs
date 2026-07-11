# Helix Incident Response

**Owner:** Helix Platform Operations  
**Audience:** On-call engineers, incident commanders, security on-call  
**Last reviewed:** 2026-07-14  
**Status:** Approved  
**Related:** [logging-and-monitoring.md](logging-and-monitoring.md), [deployment.md](deployment.md), [security.md](security.md), [authentication.md](authentication.md), [database.md](database.md)

This procedure applies to Helix production (`rg-helix-prod`) and to security events in any environment. Non-production outages are handled on Teams **Helix Platform** during business hours unless they block a production release soak.

---

## 1. Severity

| Severity | Definition | Examples |
| --- | --- | --- |
| **P1** | Complete customer-facing outage, confirmed data loss/corruption, or security breach (including break-glass use) | Gateway or core API down; Front Door origin unhealthy; ransomware-like encryption; `helix-breakglass` login; confirmed leak of HelixCore data |
| **P2** | Major degradation, authentication failing for many users, or data-plane partial failure with workaround | Entra-related 401 spike; SQL dependency failures > 2% (`Helix-Sql-Dependency-Failures`); identity 503s on all writes; p95 work-orders > 800 ms for 15 minutes **and** business impact |
| **P3** | Partial impact, workaround exists, SLO weekly miss | Single tenant misconfiguration; WAF false positives; weekly p95 SLO 400 ms miss without 800 ms alert |
| **P4** | Minor / cosmetic | Dashboard wrong tile; non-prod noise |

If unsure between P1 and P2, start as **P1** and downgrade after the first assessment (15 minutes).

---

## 2. Time targets (Europe/Stockholm clocks for “business hours”)

| Severity | Acknowledge | Mitigate / restore service | Resolve (root cause fix or accepted workaround + ticket) |
| --- | --- | --- | --- |
| P1 | 15 minutes | 4 hours | Same day mitigation; RCA ticket same week |
| P2 | 30 minutes | 8 hours | 5 business days for permanent fix plan |
| P3 | 4 business hours | 3 business days | Backlog |
| P4 | Next business day | Best effort | Backlog |

Acknowledge means an on-call engineer posts in Teams **Helix Incidents** that they own the incident and opens an Azure DevOps work item.

---

## 3. Detection and paging

Production alerts and action groups are listed in [logging-and-monitoring.md](logging-and-monitoring.md):

- P1: `ag-helix-p1-prod` (SMS + phone + Teams Helix Incidents) for `Helix-Availability-Drop` and `Helix-5xx-Rate`
- P2: `ag-helix-p2-prod` (SMS + Teams) for auth spikes, SQL failures, ready-check failures, outbox lag, work-order p95

Human reports go to email `helix-oncall@nordicsystems.internal` and must include `X-Helix-Correlation-Id` if the caller has an API error body ([api-guidelines.md](api-guidelines.md)).

Internal status page: `https://status.helix.nordicsystems.internal`. Only the Incident Commander or their deputy updates it.

---

## 4. Roles

| Role | Who | Duties |
| --- | --- | --- |
| Incident Commander (IC) | Primary on-call unless they hand over | Severity, comms, decisions (including rollback) |
| Technical lead | Secondary on-call or specialist | Diagnosis, changes |
| Comms | IC or designated | Status page, Helix Platform summary for P1 after 30 minutes |
| Security lead | Security On-Call | Required for P1 security, Key Vault, auth bypass, data leak |

On-call rota lives in Azure DevOps (Helix project wiki calendar). Do not page random engineers via personal mobile numbers outside the action groups.

---

## 5. P1/P2 working procedure

1. **Ack** in Teams **Helix Incidents** with alert name and time.
2. Create work item type **Incident**, area `Helix\Operations`, title prefix `INC-`, severity field set. Link the Application Insights alert.
3. Start the standing Teams meeting **Helix P1 Bridge** for all P1s (optional for P2 if more than two people are involved).
4. **Do not deploy new features.** Allowed production changes: slot swap **back**, scaling out App Service instances, restarting `func-helix-jobs-prod`, failing over SQL **only** with Data Platform + IC agreement, WAF rule with Security On-Call.
5. Capture correlation ids, `cloud_RoleName`, and SHA of the running image (`Helix__ImageSha` app setting) in the incident item.
6. Mitigate. Prefer rollback of the last slot swap ([deployment.md](deployment.md) §4) if the incident started within 60 minutes of a production swap.
7. When service is restored, mark incident **Mitigated**, keep the bridge until error rate is below alert thresholds for 15 minutes.
8. Resolve work item when follow-up tickets exist.

Kusto starting queries are in [logging-and-monitoring.md](logging-and-monitoring.md). Check dashboard **Helix Production Overview** first.

---

## 6. Runbooks (first checks)

### 6.1 API 5xx / availability

- Front Door origin health vs internal gateway `https://api.helix.nordicsystems.internal/v1/health`
- App Service instances vs CPU on `asp-helix-prod`
- `/v1/ready` vs SQL: if ready fails, check Key Vault references and `Helix-Sql-Dependency-Failures`
- Recent CD run on `helix-api-cd` production stage

### 6.2 Authentication (401/403)

- Alert `Helix-Auth-401-Spike`: check Entra status, token audience `api://helix-api`, clock skew on instances
- Widespread `403` on one tenant: `identity.TenantUsers` / cache; `helix-identity` health
- Writes failing `IdentityUnavailable`: `app-helix-identity-prod`, NSG, private endpoint to identity
- Break-glass: treat as P1 security immediately ([authentication.md](authentication.md))

There is no local JWT signing fallback in production ([security.md](security.md)). Do not “fix” an Entra outage by disabling authentication middleware.

### 6.3 Database

- Server `sql-helix-prod.database.windows.net`, databases HelixCore, HelixIdentity, HelixAudit
- Failover group `fog-helix-prod` to West Europe is **not** automatic application behavior; IC + Data Platform only
- PIM group `grp-helix-sql-breakglass` for human investigation; record in the incident
- Never restore production over production without Data Platform; PITR is 35 days ([database.md](database.md))

### 6.4 Jobs / messaging

- Namespace `sb-helix-prod.servicebus.windows.net`
- Queues `helix.workorder.changed`, `helix.notify.email`, `helix.audit.ingest`
- Alert `Helix-Jobs-Outbox-Lag`: function app `func-helix-jobs-prod`, identity `id-helix-jobs-prod`, table `ops.OutboxMessages`

### 6.5 Security incident extras

Page Security On-Call. Preserve logs: request 365-day hold on `log-helix-prod`. Do not recycle App Services in a way that destroys useful dumps until Security agrees. Do not rotate all secrets blindly; follow Security’s containment plan. Key Vault admin use must be logged in the incident item.

Suspected leak of connection strings or client secrets: rotate Helix-owned secrets via `helix-secret-rotation`, recycle apps, revoke Entra client secrets in the Helix-API-* registrations as directed by Security. SQL password fallback is **not** an approved containment step ([security.md](security.md)).

---

## 7. Communication templates (internal)

P1 status page: Investigating (next update 15 min) → Identified (category only: deploy, dependency, auth, data) → Resolved (UTC, SHA if deploy). Never post secrets, tokens, or customer emails on Teams.

---

## 8. Post-incident

Required for **all P1 and P2** within **5 business days**:

- Azure DevOps work item type **Postmortem**, linked to `INC-*`
- Timeline (UTC), detection path (alert name vs human)
- What monitoring missed
- Action items with owners (no orphan “improve logging”)

Template: Summary, Impact, Detection, Response, Root cause, Follow-ups (owners required). A rolled-back production deploy still needs a P2 postmortem if customers saw 5xx for more than 5 minutes.

---

## 9. Non-production

Test environment outages that block `helix-e2e-nightly` or CI integration are P3 for Platform during business hours. Do not page `ag-helix-p1-prod` for `rg-helix-test`. Staging issues during soak **do** block production promotion ([deployment.md](deployment.md) 24-hour soak) but are not production P1s unless staging is used as a production fallback (it is not).
