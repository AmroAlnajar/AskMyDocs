# Helix Authentication and Authorization

**Owner:** Helix Identity maintainers  
**Audience:** API and portal engineers  
**Last reviewed:** 2026-05-28  
**Status:** Approved  
**Related:** [architecture.md](architecture.md), [api-guidelines.md](api-guidelines.md), [security.md](security.md), [incident-response.md](incident-response.md)

Helix uses Microsoft Entra ID as the sole identity provider (ADR-022). There is no local username/password store, no API-key header scheme for partners, and no long-lived static tokens in production. This document defines how tokens are issued, validated, and mapped to Helix roles.

---

## 1. Tenant and applications

| Item | Value |
| --- | --- |
| Entra ID tenant display name | Nordic Systems |
| Issuer (v2) | `https://login.microsoftonline.com/11111111-aaaa-bbbb-cccc-222222222222/v2.0` |
| Tenant ID | `11111111-aaaa-bbbb-cccc-222222222222` |
| API App ID URI / audience | `api://helix-api` |
| Gateway OpenID metadata | Used only by services; clients talk to Entra directly |

Application registrations: **Helix-API-Prod/Stage/Test/Dev** (resource APIs; do not reuse prod client IDs), **Helix-Portal-Prod/Stage** (PKCE), **Helix-Jobs-Prod** (managed identity by default).

Application (client) IDs used in documentation and non-secret config:

- Helix-API-Prod: `aaaaaaaa-0001-4000-8000-000000000001`
- Helix-API-Stage: `aaaaaaaa-0001-4000-8000-000000000002`
- Helix-API-Test: `aaaaaaaa-0001-4000-8000-000000000003`
- Helix-API-Dev: `aaaaaaaa-0001-4000-8000-000000000004`
- Helix-Portal-Prod: `bbbbbbbb-0001-4000-8000-000000000001`

These identifiers are not credentials. Client secrets, if ever created for a confidential integration, are stored only in `kv-helix-{env}` under `Auth--Clients--{ClientName}--Secret` and rotated every 90 days ([security.md](security.md)).

---

## 2. Token acquisition

### 2.1 Interactive users (Helix Portal and field apps)

Portal uses authorization code + PKCE. Required scope: `api://helix-api/access_as_user`. Access tokens are JWT Bearer, **60 minutes** in all environments, and must not be stored in localStorage. Portal idle timeout (8 hours) is not an API concern; APIs reject expired tokens regardless.

### 2.2 Daemon / integration clients

Partner and internal batch clients use OAuth 2.0 client credentials against Entra. Required scope: `api://helix-api/access_as_service`. Tokens are 60 minutes. The client must still send `X-Helix-Tenant-Id` on every call; membership of the service principal to that tenant is stored in **HelixIdentity** table `identity.TenantClients`.

`helix-jobs` uses managed identity `id-helix-jobs-{env}` and Azure.Identity for audience `api://helix-api`. In staging and production, Functions still call through `helix-gateway`.

Forbidden: ROPC, shared integration passwords, unsigned JWT decode, any audience other than `api://helix-api`, and using **Helix-API-Dev** against test or prod.

---

## 3. Validation rules on helix-core-api and helix-identity

Middleware order: exception handler → correlation → authentication → tenant context → authorization.

JwtBearer: `ValidIssuer` as above (one Entra tenant for all Helix environments), `ValidAudience` `api://helix-api`, `ClockSkew` 2 minutes, HTTPS metadata **true** in test/stage/prod.

`401` / `Unauthorized`: missing header, expired token, bad signature, or audience/issuer mismatch.

A request is authenticated but forbidden (`403`, `Forbidden`) when the token is valid but the caller lacks the required role **or** is not a member of the tenant in `X-Helix-Tenant-Id`.

Tenant membership:

- Users: group-based plus `identity.TenantUsers` (object id, tenant id, `IsActive`)
- Service principals: `identity.TenantClients` only (groups are not used for daemon clients)

If `helix-identity` is unreachable, **write** operations fail closed with `503` / `IdentityUnavailable`. **Read** operations may use a cached membership entry that is no older than **5 minutes** (memory cache on `helix-core-api`, key `tenant:{tenantId}:oid:{objectId}`). Cache must not be used for `Helix.Administrator` elevation checks on `/v1/admin/*` routes; those always call identity live.

---

## 4. App roles and Entra groups

Helix API app roles (claimed in the `roles` array of the access token):

| App role | Entra security group | Typical users |
| --- | --- | --- |
| `Helix.Administrator` | `grp-helix-admins` | Platform and tenant admins |
| `Helix.Operator` | `grp-helix-operators` | Dispatchers, planners |
| `Helix.Support` | `grp-helix-support` | Internal support |
| `Helix.Reader` | `grp-helix-readers` | Finance and reporting |
| `Helix.Integration` | `grp-helix-integrations` | Daemon app registrations only |

Role assignment is done in Entra (app role assignment to the group). Helix does not implement a custom permission table for these five roles. Finer-grained per-tenant feature flags live in HelixCore `core.TenantFeatures` and never override a `403` from missing app roles.

Authorization matrix (summary; controllers use `[Authorize(Roles = "...")]` plus handler checks):

| Capability | Admin | Operator | Support | Reader | Integration |
| --- | --- | --- | --- | --- | --- |
| `GET /v1/customers`, `GET /v1/work-orders`, `GET /v1/assets`, `GET /v1/contracts` | Yes | Yes | Yes | Yes | Yes |
| `POST /v1/customers`, `POST /v1/work-orders`, `PATCH /v1/work-orders/{id}` | Yes | Yes | No | No | Yes |
| `POST /v1/assets/{id}/events` | Yes | Yes | No | No | Yes |
| Soft-delete customer or contract | Yes | No | No | No | No |
| `GET /v1/admin/tenants` | Yes | No | No | No | No |
| Read HelixAudit via API | Yes | No | Yes (own tenant, last 30 days) | No | No |

The API does not accept `X-Impersonate-User`. Portal “support view” still sends a Support token.

---

## 5. Required HTTP headers

In addition to `Authorization: Bearer {token}`:

| Header | Required | Rule |
| --- | --- | --- |
| `X-Helix-Tenant-Id` | Yes (except `/v1/health` and `/v1/ready`) | GUID; must match a tenant the caller belongs to |
| `X-Helix-Correlation-Id` | Recommended | UUIDv4; gateway generates if omitted |
| `X-Api-Version` | No | Default `1.0` |

Health endpoints are anonymous but are not routed through Front Door to the public internet; they are used by App Service health checks (`/v1/health`) as configured in [deployment.md](deployment.md).

---

## 6. Service-to-service on the internal network

`helix-core-api` calling `helix-identity` `/internal/tenants/{tenantId}/members/{objectId}` uses managed identity `id-helix-api-{env}` and audience `api://helix-identity` (app ID URI of the identity resource API). This audience is **not** `api://helix-api`. Identity’s internal app registration names are `Helix-Identity-Prod` (and Stage/Test/Dev). NSGs prevent this internal URL from being reached from the public internet.

---

## 7. Break-glass and incidents

Break-glass Entra account `helix-breakglass` is stored in the offline safe procedure owned by Corporate IT, not in Key Vault. Use of break-glass is a **P1 security event** and must be reported through [incident-response.md](incident-response.md) within 15 minutes of use.

If Entra ID is unavailable, Helix cannot authenticate users. There is no local JWT signing key fallback in staging or production. The Key Vault secret `Auth--Jwt--Audience` is configuration (the string `api://helix-api`), not a signing key. Token signing is performed by Entra ID only.

Authentication outage symptoms, alert names, and paging are in [logging-and-monitoring.md](logging-and-monitoring.md) (alert `Helix-Auth-401-Spike`). Treat a sustained 401 spike with valid client configuration as a suspected Entra or clock-skew incident; treat a 403 spike on a single tenant as possible membership cache or `TenantUsers` data issue.
