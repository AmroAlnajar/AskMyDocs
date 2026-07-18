# Helix Deployment Procedures

**Owner:** Platform Engineering  
**Audience:** Developers, release approvers, on-call  
**Last reviewed:** 2026-07-02  
**Status:** Approved  
**Related:** [architecture.md](architecture.md), [security.md](security.md), [development-workflow.md](development-workflow.md), [testing.md](testing.md), [incident-response.md](incident-response.md)

This document describes how Helix services are built, published, and released to Azure. Repositories involved: `helix-api`, `helix-identity`, `helix-jobs`, `helix-infra`. Azure DevOps organization: `https://dev.azure.com/nordic-systems`, project **Helix**.

---

## 1. Artifacts and container registry

All runtime images are pushed to Azure Container Registry **`acrhelixnordic.azurecr.io`**.

| Image | Repository | Tag |
| --- | --- | --- |
| Core API | `acrhelixnordic.azurecr.io/helix-api` | Git SHA (full 40 hex) and `release-{semver}` |
| Gateway | `acrhelixnordic.azurecr.io/helix-gateway` | Same |
| Identity | `acrhelixnordic.azurecr.io/helix-identity` | Same |
| Jobs | `acrhelixnordic.azurecr.io/helix-jobs` | Same |

Images are multi-stage Docker builds based on .NET 10 chiseled ASP.NET images, run as non-root UID **1654**, listen on port **8080** (`WEBSITES_PORT=8080`). ACR is in Sweden Central. Production App Services pull via private endpoint; identities `id-helix-api-{env}` (and siblings) have `AcrPull`. Do not use `:latest` in staging or production.

Infrastructure is Bicep in `helix-infra`, pipeline **helix-infra-cd**. Application pipelines never create Key Vaults, SQL servers, or VNets.

---

## 2. Pipelines

| Pipeline | File | Purpose |
| --- | --- | --- |
| helix-api-ci | `helix-api` `/build/helix-api-ci.yml` | Restore, build, unit + integration tests, OpenAPI artifact, vulnerability scan, coverage gate |
| helix-api-cd | `/build/helix-api-cd.yml` | Build image, push ACR, deploy environment |
| helix-identity-ci / cd | analogous | Identity service |
| helix-jobs-ci / cd | analogous | Functions package + image |
| helix-infra-cd | `helix-infra` | Bicep what-if then apply |
| helix-e2e-nightly | — | Playwright against **test** at 02:00 Europe/Stockholm |
| helix-secret-rotation | — | 90-day secret rotation helper (does not rotate Entra signing; Entra-owned) |

CI: Microsoft-hosted `ubuntu-24.04`. SQL integration uses Testcontainers (`mcr.microsoft.com/mssql/server:2022-latest`), not Azure SQL. Gates: [testing.md](testing.md). CD requires green CI on the same SHA.

---

## 3. Environment promotion

| Environment | Resource group | App Service examples | Trigger |
| --- | --- | --- | --- |
| Development | `rg-helix-dev` | `app-helix-api-dev`, `app-helix-gateway-dev`, `app-helix-identity-dev`, `func-helix-jobs-dev` | Automatic on merge to `develop` |
| Test | `rg-helix-test` | `*-test` | Automatic after successful **dev** deploy of the same SHA, plus CI green |
| Staging | `rg-helix-stage` | `*-stage` | Manual pipeline run from a `release/x.y.z` branch or from `main` pre-prod tag |
| Production | `rg-helix-prod` | `*-prod` plus slot `slot-preprod` | Manual, two approvers, after staging soak |

Subscriptions: non-prod `sub-nordic-helix-nonprod`, prod `sub-nordic-helix-prod`.

**Staging soak:** the candidate SHA must run in staging for at least **24 hours** with no open P1/P2 Helix incidents attributed to that SHA. Soak clock starts when `helix-api-cd` finishes staging (including migrator). Exception: security hotfix (see §7) may shorten soak to **2 hours** with Head of Platform written approval in the release work item.

Production change window: **Tuesday–Thursday 07:00–09:00 Europe/Stockholm**, excluding company shutdown weeks. Hotfixes may occur outside the window but still require the approval group.

Approvers: Azure DevOps environment **production** requires two members of `grp-helix-release-approvers`. The person who queued the release cannot be both approvers. CODEOWNERS merge rules for `main` are in [development-workflow.md](development-workflow.md).

---

## 4. Production slot swap (App Services)

Production App Services (`app-helix-api-prod`, `app-helix-gateway-prod`, `app-helix-identity-prod`) use deployment slot **`slot-preprod`**.

Sequence for a normal production release of `helix-api` (gateway and identity have the same slot names and must be released in the order below when APIs are incompatible):

1. Confirm CI artifact SHA and that [testing.md](testing.md) gates passed.
2. Run **Helix.Migrator** against production HelixCore / HelixIdentity / HelixAudit using `id-helix-migrator-prod`. Migrations are forward-only in this step; they must be backward compatible with the **currently live** API for the duration of the slot warmup (expand/contract).
3. Deploy new image to `slot-preprod` (not to production traffic).
4. Slot App Settings: `ASPNETCORE_ENVIRONMENT=Production`, Key Vault references identical to production except `Helix__SlotName=preprod` (used only in logs).
5. Warmup: health check **`/v1/health`**, `WEBSITE_HEALTHCHECK_MAXFAILED=3`. Smoke also calls `/v1/ready` using OIDC federated credential `ado-helix-cd-prod` (variable group `vg-helix-prod-smoke`; no passwords).
6. Swap `slot-preprod` with production.
7. Monitor Application Insights `appi-helix-prod` for 30 minutes (see [logging-and-monitoring.md](logging-and-monitoring.md)).
8. If swap is bad: **swap back immediately**. Do not forward-fix on production during a P1. See [incident-response.md](incident-response.md).

Functions (`func-helix-jobs-prod`) have no slots. CD deploys a new revision and keeps previous zip/image for **three** successful releases to allow manual rollback via pipeline stage **Rollback-jobs**.

Deploy services in this order when the release notes say “contract change”: **identity → core-api → gateway → jobs**. When only core-api changes: **core-api → jobs** (gateway can stay). Never deploy gateway before core-api if new routes were added.

---

## 5. Configuration: App Settings vs Key Vault

Non-secret App Settings (Bicep): `ASPNETCORE_ENVIRONMENT` (`Development`|`Test`|`Staging`|`Production`), `Helix__ServiceName`, `Helix__PublicBaseUrl`, `AzureAd__Instance` (`https://login.microsoftonline.com/`), `AzureAd__TenantId` (`11111111-aaaa-bbbb-cccc-222222222222`), `AzureAd__ClientId` (env API registration, [authentication.md](authentication.md)), `AzureAd__Audience` (`api://helix-api`), `WEBSITES_PORT=8080`, `WEBSITES_ENABLE_APP_SERVICE_STORAGE=false`.

Secrets and connection strings are Key Vault references, not pipeline secret variables:

`@Microsoft.KeyVault(VaultName=kv-helix-prod;SecretName=Sql--HelixCore--ConnectionString)`

Vault names: `kv-helix-dev`, `kv-helix-test`, `kv-helix-stage`, `kv-helix-prod`. Required secret names are listed in [security.md](security.md). The App Service user-assigned identity must have `Get`/`List` on that vault **before** the first deploy or the site will fail ready checks.

Variable group `vg-helix-prod-kv` in Azure DevOps is linked to `kv-helix-prod` for pipeline steps that run migrator; it is not a place to paste connection strings.

Always On is **enabled** on staging and production App Services. Dev and test may scale to zero only if a platform experiment is documented; default is one small instance always on for test because nightly e2e depends on it.

---

## 6. Environment differences (operations)

| Topic | Dev | Test | Stage | Prod |
| --- | --- | --- | --- | --- |
| Instances (API/gateway) | 1 | 1 | 2 | min 3 |
| SKU | P1v3 | P1v3 | P1v3 | P2v3 |
| Slot swap | No | No | No | Yes |
| Direct API hostname bypass gateway | Allowed from engineering subnet | Allowed from Azure DevOps agents | Blocked by NSG | Blocked |
| Log level default | Debug | Information | Information | Information (see logging doc for sampling) |
| WAF / Front Door | No | No | No | `afd-helix-prod` |
| Deploy from branch | `develop` | SHA already on develop | `release/*` or `main` | `main` only |

Production SQL firewall does not allow developer public IPs. Access is private endpoint plus PIM as in [database.md](database.md).

---

## 7. Hotfix and forbidden actions

Hotfix: `hotfix/*` from `main` per [development-workflow.md](development-workflow.md), CI, shortened soak, slot swap. Forbidden: laptop `az webapp deploy` to prod; portal App Settings edits except active P1 with IC approval (Bicep within 24h); images not in `acrhelixnordic.azurecr.io`; skipping migrator. After prod deploy, post SHA and pipeline run id to Teams **Helix Platform**.
