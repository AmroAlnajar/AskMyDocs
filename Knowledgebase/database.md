# Helix Database Standards

**Owner:** Helix Data Platform  
**Audience:** API, jobs, and infrastructure engineers  
**Last reviewed:** 2026-06-18  
**Status:** Approved  
**Related:** [architecture.md](architecture.md), [security.md](security.md), [deployment.md](deployment.md), [logging-and-monitoring.md](logging-and-monitoring.md)

Helix persists business data in Azure SQL Database. This document is mandatory for schema changes in repositories `helix-api`, `helix-identity`, and `helix-jobs`.

---

## 1. Servers, databases, and environments

| Environment | Logical SQL server | Resource group | Databases |
| --- | --- | --- | --- |
| Development | `sql-helix-nonprod.database.windows.net` | `rg-helix-dev` | Suffixed copies (see below) |
| Test | `sql-helix-nonprod.database.windows.net` | `rg-helix-test` | HelixCore, HelixIdentity, HelixAudit |
| Staging | `sql-helix-nonprod.database.windows.net` | `rg-helix-stage` | HelixCore, HelixIdentity, HelixAudit |
| Production | `sql-helix-prod.database.windows.net` | `rg-helix-prod` | HelixCore, HelixIdentity, HelixAudit |

Non-production databases for different environments are **separate Azure SQL databases** on the shared logical server `sql-helix-nonprod`. Names:

- Dev: `HelixCore_dev`, `HelixIdentity_dev`, `HelixAudit_dev`
- Test: `HelixCore_test`, `HelixIdentity_test`, `HelixAudit_test`
- Stage: `HelixCore_stage`, `HelixIdentity_stage`, `HelixAudit_stage`
- Prod: `HelixCore`, `HelixIdentity`, `HelixAudit` (no suffix)

Production uses elastic pool `epool-helix-prod`. Non-prod uses per-database provisioned SKUs (General Purpose, 2 vCores) except staging HelixCore which is 4 vCores to approximate production plans.

Primary region is **Sweden Central**. Production geo-replication target is **West Europe** (failover group `fog-helix-prod`). Failover is a coordinated operations action, not an automatic application retry against the secondary for writes.

Collation for all Helix databases: **Finnish_Swedish_CI_AS**. Compatibility level: **170**. `READ_COMMITTED_SNAPSHOT` is **on**.

---

## 2. Authentication to SQL

| Environment | SQL authentication |
| --- | --- |
| Local workstation | SQL Server LocalDB, database `HelixCore`, Windows authentication or local SQL user `HelixDevLocal` (password only in user secrets) |
| Dev / Test / Stage / Prod | Azure AD user for the service’s user-assigned managed identity |

Managed identity names that must exist as users in each database:

- `id-helix-api-{env}` — HelixCore (DML), HelixAudit (INSERT only on `audit` schema)
- `id-helix-identity-{env}` — HelixIdentity (DML), HelixCore (`SELECT` on `core.Tenants` only)
- `id-helix-jobs-{env}` — HelixCore (DML on `ops` and selected `core` tables), HelixAudit (INSERT)

No SQL passwords in shared environments. Key Vault secrets `Sql--HelixCore--ConnectionString`, `Sql--HelixIdentity--ConnectionString`, and `Sql--HelixAudit--ConnectionString` use `Authentication=Active Directory Managed Identity` plus the user-assigned identity client id. Injection: [deployment.md](deployment.md), [security.md](security.md).

Default ADO.NET settings in Helix:

- `Max Pool Size=100`
- `Connect Timeout=15`
- Command timeout **30 seconds** for APIs, **120 seconds** for `helix-jobs` report/aggregation functions
- `Application Name=helix-core-api` (or `helix-identity`, `helix-jobs`) for `sys.dm_exec_sessions`

---

## 3. Schemas

| Schema | Database | Purpose |
| --- | --- | --- |
| `core` | HelixCore | Tenants, customers, contracts, assets, work orders, tenant features |
| `ops` | HelixCore | Outbox, idempotency keys, job leases |
| `identity` | HelixIdentity | `TenantUsers`, `TenantClients`, client registry metadata |
| `audit` | HelixAudit | Append-only access and admin action logs |

Do not add tables to `dbo` except EF Core history `__EFMigrationsHistory`.

---

## 4. Object naming

- Tables: PascalCase plural (`Customers`, `WorkOrders`, `Assets`, `Contracts`, `OutboxMessages`, `IdempotencyKeys`)
- PK: `{Singular}Id` `uniqueidentifier` (`NEWSEQUENTIALID()` allowed; no `IDENTITY` for tenant entities)
- FKs `FK_{Child}_{Parent}`; indexes `IX_{Table}_{Columns}` (example `IX_WorkOrders_TenantId_Status_ScheduledStartUtc`); unique `UX_Customers_TenantId_ExternalReference`
- Prefer application code over stored procedures (`usp_{Schema}_{Action}` if unavoidable)

Every tenant-scoped table **must** include `TenantId uniqueidentifier not null` as the leading column of the clustered index unless a documented exception exists. Current clustered pattern: `CLUSTERED (TenantId, {Table}Id)`.

---

## 5. Required columns

Tenant-scoped tables include:

| Column | Type | Notes |
| --- | --- | --- |
| `TenantId` | uniqueidentifier | Not null |
| `{Entity}Id` | uniqueidentifier | PK; follow existing `WorkOrders` clustered pattern |
| `CreatedUtc` | datetime2(7) | Set in application, UTC |
| `CreatedBy` | nvarchar(64) | Entra object id or client id |
| `ModifiedUtc` | datetime2(7) | |
| `ModifiedBy` | nvarchar(64) | |
| `RowVersion` | rowversion | Concurrency token; exposed as ETag in API |
| `IsDeleted` | bit | Soft delete default 0 |
| `DeletedUtc` | datetime2(7) null | |

Hard delete is forbidden on `core` tables except by the documented GDPR erasure job `GdprEraseCustomer` in `helix-jobs`, which runs only after Legal ticket `HLX-LEGAL-*` and writes a tombstone to HelixAudit.

Store all timestamps as UTC. Display time zone for Nordic Systems HQ is **Europe/Stockholm**; conversion is a client concern.

---

## 6. Core tables (reference)

HelixCore `core` (non-exhaustive but stable):

- `Tenants` — `TenantId`, `Name`, `Status` (`Active`/`Suspended`), `CreatedUtc`
- `Customers` — `CustomerId`, `TenantId`, `Name`, `ExternalReference`, `Status`, PII columns `PrimaryContactName`, `PrimaryContactEmail`
- `Contracts` — `ContractId`, `TenantId`, `CustomerId`, `StartUtc`, `EndUtc`, `Status`
- `Assets` — `AssetId`, `TenantId`, `CustomerId`, `SerialNumber`, `SiteCode`
- `WorkOrders` — status, priority, schedule, `AssigneeObjectId`, `ExternalReference`
- `WorkOrderEvents` — child of work orders; not the same as asset events
- `AssetEvents` — `AssetEventId`, `TenantId`, `AssetId`, `Type`, `OccurredUtc`, `PayloadJson` nvarchar(max) with 32 KB app limit
- `TenantFeatures` — feature flags per tenant

`ops.OutboxMessages`: `OutboxMessageId`, `TenantId`, `Type`, `PayloadJson`, `OccurredUtc`, `ProcessedUtc` null, `Attempts`. Dispatcher in `helix-jobs` is the only processor.

`ops.IdempotencyKeys`: `TenantId`, `IdempotencyKey`, `RequestHash`, `ResponseJson`, `CreatedUtc`. Unique `(TenantId, IdempotencyKey)`. Retention 24 hours; cleanup function `IdempotencyCleanup` nightly 01:15 Europe/Stockholm.

QA tenant used by automated tests: `33333333-3333-3333-3333-333333333333` (`Helix QA Tenant`) in **test** databases only. Never create this tenant in production.

---

## 7. Migrations

EF Core migrations live in `Helix.Core.Infrastructure`. Pipeline `helix-api-cd` applies migrations as a **job** (`Helix.Migrator` console) **before** App Service slot swap in production, and before starting new containers in other environments. The migrator uses identity `id-helix-migrator-{env}` with `db_ddladmin` plus DML. API identities must **not** have `ALTER` permission.

Rules:

- One business change per migration when possible
- No destructive column drop in the same release that still reads the column; two-release deprecation
- Backfills over 50,000 rows belong in `helix-jobs`, not in a blocking migration
- Indexes added `ONLINE = ON` in production scripts (EF custom SQL) for tables over 1 million rows

---

## 8. Backup, PITR, and data movement

| Environment | Point-in-time retention | Geo |
| --- | --- | --- |
| Dev | 7 days | Local redundant |
| Test | 7 days | Local redundant |
| Stage | 14 days | Local redundant |
| Prod | 35 days | Geo-redundant + geo-replica |

Restoring production data into non-prod is **forbidden** unless Privacy has approved anonymization. Staging receives a monthly anonymized subset via pipeline `helix-db-anonymize` (project Helix). Test and dev use synthetic generators.

---

## 9. Performance and monitoring

Application Insights dependency tracking name for SQL is the server + database. Alert `Helix-Sql-Dependency-Failures` fires when SQL dependency failure rate exceeds **2%** for 10 minutes ([logging-and-monitoring.md](logging-and-monitoring.md)).

Query expectations:

- `GET /v1/work-orders` list must use `IX_WorkOrders_TenantId_Status_ScheduledStartUtc`
- Cross-tenant queries are forbidden in application code; `IgnoreQueryFilters` is allowed only in migrator and GDPR job
- Deadlocks: retry once with jitter in Infrastructure; still log `Warning`

Long-running investigative queries go through Azure Data Studio against **read-only** replicas when available; production writable access for humans is PIM-eligible group `grp-helix-sql-breakglass` (max 4 hours), which is a security-relevant event.
