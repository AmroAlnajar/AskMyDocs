# Helix Security Requirements

**Owner:** Nordic Systems Security Engineering (embedded in Helix)  
**Audience:** All Helix engineers and release approvers  
**Last reviewed:** 2026-07-09  
**Status:** Approved  
**Related:** [architecture.md](architecture.md), [authentication.md](authentication.md), [deployment.md](deployment.md), [database.md](database.md), [incident-response.md](incident-response.md)

This document states the security baseline for Helix. Exceptions require a written risk acceptance in Azure DevOps work item type **Risk**, area `Helix\Security`, and expire after 90 days.

---

## 1. Classification and trust boundaries

Tenant business data (customers, contacts, contracts, work orders, assets) is classified **Confidential**. HelixAudit records of who accessed PII are **Confidential**. Infrastructure configuration without secrets is **Internal**.

Trust boundaries:

- Public: Azure Front Door `afd-helix-prod` → `https://api.nordicsystems.com/helix/v1`
- Internal corporate: Private DNS `helix.nordicsystems.internal`
- Data plane: Azure SQL, Key Vault, Service Bus, ACR — private endpoints in production

Production App Services and Functions **must not** have a public inbound IP for management. SCM/Kudu is limited to Azure DevOps and the Platform Engineering jump specification (corporate IT).

---

## 2. Network

Production VNet `vnet-helix-prod` address space **`10.40.0.0/16`** (Sweden Central):

| Subnet | CIDR | Use |
| --- | --- | --- |
| `snet-apps-prod` | `10.40.1.0/24` | App Service VNet integration (gateway, api, identity) |
| `snet-func-prod` | `10.40.3.0/24` | Functions VNet integration |
| `snet-pe-prod` | `10.40.2.0/24` | Private endpoints (SQL, Key Vault, ACR, Service Bus, App Insights) |

NSG `nsg-helix-apps-prod` denies inbound from Internet to app subnets. Staging uses the non-production VNet `vnet-helix-nonprod` **`10.50.0.0/16`** with analogous subnets `snet-apps-nonprod` `10.50.1.0/24`, `snet-pe-nonprod` `10.50.2.0/24`, `snet-func-nonprod` `10.50.3.0/24`.

Direct calls to `app-helix-api-*` hostnames from developer workstations are allowed in **dev** and **test** from the engineering subnet only. In **staging** and **production**, NSG plus private ingress require traffic through `helix-gateway` ([architecture.md](architecture.md)).

Outbound from apps is restricted via route and firewall rules to: Azure SQL, Key Vault, Service Bus, Application Insights, ACR pull, and Microsoft Entra ID / Graph (identity service only). Arbitrary Internet egress is denied in production.

---

## 3. TLS, HTTP, and CORS

- Minimum TLS **1.2**; TLS 1.3 preferred on Front Door and App Service
- HTTPS only; HTTP redirect on Front Door
- HSTS **365 days** on production public endpoint
- Certificates: Key Vault certificate `helix-api-tls-{env}` bound through Front Door / App Service. Do not store PFX in Git.

CORS allow-list (exact origins, **no wildcard** in staging or production):

- `https://portal.helix.nordicsystems.internal`
- `https://portal-stage.helix.nordicsystems.internal`

Test/dev may also allow `https://portal-test.helix.nordicsystems.internal` and `http://localhost:5173`. `AllowCredentials` is true for portal origins only. Partner calls are server-side (no extra CORS origins without review).

---

## 4. Key Vault and secrets

Vaults: `kv-helix-dev`, `kv-helix-test`, `kv-helix-stage`, `kv-helix-prod` (Sweden Central). Production vault is not readable by non-prod identities.

Required secret **names** (values never documented):

| Secret name | Purpose |
| --- | --- |
| `Sql--HelixCore--ConnectionString` | Azure AD Managed Identity connection string |
| `Sql--HelixIdentity--ConnectionString` | Same pattern |
| `Sql--HelixAudit--ConnectionString` | Same pattern |
| `Auth--Jwt--Audience` | Literal audience config `api://helix-api` |
| `Auth--Entra--TenantId` | `11111111-aaaa-bbbb-cccc-222222222222` |
| `Auth--Clients--{ClientName}--Secret` | Optional confidential client secrets |
| `AppInsights--ConnectionString` | Telemetry |
| `ServiceBus--Helix--ConnectionString` | When not using MSI-only; prod prefers MSI |

There is **no** Helix-issued JWT signing key in Key Vault. Tokens are signed by Entra ID ([authentication.md](authentication.md)).

Access policy / RBAC: user-assigned identities `id-helix-gateway-{env}`, `id-helix-api-{env}`, `id-helix-identity-{env}`, `id-helix-jobs-{env}`, `id-helix-migrator-{env}` get **Key Vault Secrets User** (`Get`, `List`). Human access to `kv-helix-prod` is PIM group `grp-helix-kv-prod-readers` (read) and `grp-helix-kv-prod-admins` (write), maximum 4 hours.

App Service must use Key Vault **references** as in [deployment.md](deployment.md), not copied secret values in App Settings. Azure DevOps variable group `vg-helix-prod-kv` is vault-linked. Pipeline secret variables that duplicate SQL connection strings are forbidden.

Rotation: every **90 days** via pipeline `helix-secret-rotation` for client secrets Helix owns. SQL uses managed identity (no password rotation). After rotation, recycle App Service slots so references refresh.

Encryption at rest: Azure SQL TDE on; production uses CMK in Key Vault key **`helix-sql-cmk`**. Service Bus and ACR use Microsoft-managed keys unless Finance requires CMK (not currently).

---

## 5. Identity, roles, and data access

Entra tenant `11111111-aaaa-bbbb-cccc-222222222222` is the only IdP. Role names and groups (`grp-helix-admins`, `grp-helix-operators`, `grp-helix-support`, `grp-helix-readers`, `grp-helix-integrations`) are defined in [authentication.md](authentication.md).

Application rules:

- Fail closed on identity outage for writes (`503` / `IdentityUnavailable`)
- No `X-Impersonate-User` header
- Soft-delete of customers/contracts is `Helix.Administrator` only
- Cross-tenant data access in application code is forbidden ([database.md](database.md) query filters)
- Production data in test/dev is forbidden; staging uses anonymized subset only

PII columns (`PrimaryContactName`, `PrimaryContactEmail`) when returned on GET customer must generate an audit insert to HelixAudit (async via queue `helix.audit.ingest`). Support role may read audit for their tenant for the last 30 days; export of audit is admin-only.

Break-glass Entra user `helix-breakglass` is owned by Corporate IT. Any use is a P1 security incident ([incident-response.md](incident-response.md), [authentication.md](authentication.md)).

---

## 6. Application and supply chain

- TLS for all service-to-service calls
- Request body size limits as in API guidelines (32 KB asset events)
- Microsoft Security DevOps / dependency scan in `helix-api-ci`; **High** or **Critical** CVE on direct dependencies **fails CI**
- Container images: non-root UID 1654, chiseled base, no Docker socket mount
- No `latest` tags in stage/prod ([deployment.md](deployment.md))
- Secrets scanning (CredScan) on the `helix-api` pipeline; findings block merge to `develop` and `main`

WAF on `afd-helix-prod`: **Prevention** mode, OWASP **3.2**. Alert `Helix-FrontDoor-WAF-Blocked` is P3 unless combined with availability drop. Do not disable WAF during incidents without Security On-Call; use a custom rule exception with expiry.

Annual penetration test is scheduled in Q1 against **staging** and a Front Door production **read-only** window coordinated with Platform. Testers must not target `sql-helix-prod` with destructive tools.

---

## 7. Deployment-time controls

Production deploys require two `grp-helix-release-approvers` members ([deployment.md](deployment.md)). Portal edits to production configuration without Bicep follow-up within 24 hours violate this baseline.

Federated credential `ado-helix-cd-prod` is the only CD identity allowed to obtain smoke-test tokens. Do not create long-lived PATs with `Contributor` on `rg-helix-prod`.

---

## 8. Logging and monitoring (security)

Do not send secrets or tokens to Application Insights. Security-relevant alerts include `Helix-Auth-401-Spike` and WAF blocks ([logging-and-monitoring.md](logging-and-monitoring.md)). Suspected credential stuffing on the public endpoint is a Security incident even if availability remains high.

If Key Vault `Get` failures spike, treat as P2 (`Helix-Ready-Fail` will likely fire because connection strings will not resolve). Do not switch production to SQL passwords as a workaround.

---

## 9. Developer workstations

Local user secrets are allowed. `appsettings.Development.json` must not contain production or staging connection strings. Pointing a local `helix-core-api` at `sql-helix-prod` is a policy violation and is blocked by SQL firewall/private endpoint in any case.

Azure CLI access to prod is PIM-eligible. Copying a production database bacpac to a laptop is forbidden.
