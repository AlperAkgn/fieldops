# Phase 2 implementation and verification

2026-11-07 · Europe/Istanbul

This report records the initial 215-test Phase 2 delivery. A later independent review found missing before/after audit values for scheduling and assignments. [Subsequent remediation and verification](phase-2-audit-remediation.md)

## Delivered scope

| Module | Implemented behavior |
| --- | --- |
| Customers | Create, detail, partial update, archive, search, type and active-state filters, stable bounded pagination. |
| Service locations | Customer-scoped addresses, contacts and validated paired coordinates, partial update, archive and filtered lists. |
| Assets and categories | Tenant-local normalized unique codes and category names, equipment and warranty details, partial update, archive and completed service history. |
| Work orders | Server-generated unique numbers, Draft creation, editable details, related customer/location/asset summaries, filters, pagination and version conflicts. |
| Assignments | Multiple active same-tenant technicians, exactly one primary, retained assignment history, serialized overlap checks, adjacent intervals and reasoned manager overrides. |
| State machine | The specified seven-state transition graph, server timestamps, immutable terminal orders and transactional status history. |
| Comments | Author and timestamp, versioned append, paged reads and restricted internal visibility. |
| Checklists | Versioned templates and items, archive, one-time immutable work-order snapshots, progress and required-item completion gates. |
| Audit | Tenant-scoped actor/resource/action/reason/time records in the business transaction, paged administrative reads and filters. |
| API quality | Safe validation and conflict responses, operation policies, parent-resource checks, OpenAPI, cancellation propagation and strict JSON fields. |

## Architecture and decisions

The four-layer modular monolith remains the implementation boundary. Domain owns the rules, Application exposes DTOs and interfaces, Infrastructure executes PostgreSQL use cases, and API composes routes and policies. No additional runtime packages were introduced.

Operational writes acquire the organization row lock before reading mutable state and revalidate session and role after waiting. This serializes assignment, rescheduling, archive and related membership checks within a tenant. Business changes, status history and audit commit together. Bigint versions provide client conflict detection in addition to EF concurrency predicates. A failed audit insert rolls back the business write.

Technicians access only assigned work orders and their necessary customer/address/asset summaries. General catalog reads require an Owner, Manager or Viewer. Internal comments and audit reads require an Owner or Manager. Availability is checked within the active tenant using half-open intervals `[start,end)`.

Catalog relationships are fixed at creation to preserve service history. Work-order references can change in Draft. Archive retains rows and relationships. Existing checklist snapshots retain copied item definitions and template version when the source changes.

## Migration and data preservation

`20261107100957_CoreOperations` adds twelve operational tables, tenant-composite foreign keys, positive-version and interval constraints, and filtered assignment uniqueness. Its downgrade refuses to drop operational and audit history. Tests exercised fresh application, populated Phase 1 upgrades, repeated application and refused downgrade with retained operational rows.

The default development database had only the first two migrations applied when inspected. The two Phase 1 security migrations and the new Phase 2 migration require the planned rollout. Development and validation used the separate `fieldops-phase2-check` project. [Default database inventory and rollout plan](phase-2-migration-plan.md)

## Verification

| Check | Observed result |
| --- | --- |
| Locked restore and .NET Release build | Passed, zero build warnings and errors. |
| Format verification | Passed for the solution. |
| Unit tests | 92 passed, zero failures or skips. Includes all 49 status pairs, lifecycle rules, catalog bounds, snapshot immutability and pagination. |
| PostgreSQL integration tests | 123 passed, zero failures or skips, using PostgreSQL 18.6 and per-test databases. Includes all Phase 0 and Phase 1 regression cases. |
| Business acceptance | A Manager created customer → location → asset → work order, applied a checklist, scheduled and assigned a Technician, and completed the lifecycle through the HTTP test host. Required items gated completion. |
| Security and concurrency | Cross-tenant and same-customer FK rejection, unassigned access, hidden internal comments, stale versions, concurrent assignment/rescheduling, primary replacement, role revalidation and audit rollback passed. |
| Clients | Both lint and strict typechecks, 23 web and 23 mobile tests, web build, Expo dependency compatibility, Android/iOS export and live Metro bundle smoke passed during this Phase 2 task. |
| Workflow syntax | `actionlint` passed. |
| Docker and trusted HTTPS | Linux image built and all five migrations applied. The real API passed certificate-validated core workflow smoke through completion, audit, service history and archive preservation. The existing web CSRF/login/refresh/logout/rate-limit smoke also passed with local Mailpit delivery. |

The initial full Release run passed 88 unit and 123 integration cases. Four catalog boundary cases were then added and the unit suite passed 92 cases. Production code did not change between these verification runs. The EF model check reported no pending changes, and migration reapplication succeeded.

## Operational limits

The organization lock deliberately serializes writes within a tenant. Measure contention before splitting the lock across aggregates or technician schedules. List totals and pages reflect ordinary read-committed queries and can move under concurrent changes.

Hosted GitHub Actions, external SMTP delivery and native device execution require their respective environments. Local SMTP is captured by Mailpit. The existing default database upgrade remains a rollout task.

Phase 3 requires separate authorization.

## Source inventory

52 created files and 13 updated files. The user's separate AGENTS.md execution-policy update was preserved.

### Domain and application

- [backend/src/FieldOps.Application/Catalog/CatalogContracts.cs](../backend/src/FieldOps.Application/Catalog/CatalogContracts.cs) · created
- [backend/src/FieldOps.Application/Catalog/UpdateCatalogRequests.cs](../backend/src/FieldOps.Application/Catalog/UpdateCatalogRequests.cs) · created
- [backend/src/FieldOps.Application/Operations/AuditContracts.cs](../backend/src/FieldOps.Application/Operations/AuditContracts.cs) · created
- [backend/src/FieldOps.Application/Operations/Pagination.cs](../backend/src/FieldOps.Application/Operations/Pagination.cs) · created
- [backend/src/FieldOps.Application/WorkOrderDetails/WorkOrderDetailContracts.cs](../backend/src/FieldOps.Application/WorkOrderDetails/WorkOrderDetailContracts.cs) · created
- [backend/src/FieldOps.Application/WorkOrders/IWorkOrderService.cs](../backend/src/FieldOps.Application/WorkOrders/IWorkOrderService.cs) · created
- [backend/src/FieldOps.Domain/Catalog/Asset.cs](../backend/src/FieldOps.Domain/Catalog/Asset.cs) · created
- [backend/src/FieldOps.Domain/Catalog/AssetCategory.cs](../backend/src/FieldOps.Domain/Catalog/AssetCategory.cs) · created
- [backend/src/FieldOps.Domain/Catalog/CatalogFields.cs](../backend/src/FieldOps.Domain/Catalog/CatalogFields.cs) · created
- [backend/src/FieldOps.Domain/Catalog/Customer.cs](../backend/src/FieldOps.Domain/Catalog/Customer.cs) · created
- [backend/src/FieldOps.Domain/Catalog/ServiceLocation.cs](../backend/src/FieldOps.Domain/Catalog/ServiceLocation.cs) · created
- [backend/src/FieldOps.Domain/Operations/AuditLog.cs](../backend/src/FieldOps.Domain/Operations/AuditLog.cs) · created
- [backend/src/FieldOps.Domain/WorkOrderDetails/ChecklistTemplate.cs](../backend/src/FieldOps.Domain/WorkOrderDetails/ChecklistTemplate.cs) · created
- [backend/src/FieldOps.Domain/WorkOrderDetails/ChecklistTemplateItem.cs](../backend/src/FieldOps.Domain/WorkOrderDetails/ChecklistTemplateItem.cs) · created
- [backend/src/FieldOps.Domain/WorkOrderDetails/DetailRuleException.cs](../backend/src/FieldOps.Domain/WorkOrderDetails/DetailRuleException.cs) · created
- [backend/src/FieldOps.Domain/WorkOrderDetails/WorkOrderChecklistItem.cs](../backend/src/FieldOps.Domain/WorkOrderDetails/WorkOrderChecklistItem.cs) · created
- [backend/src/FieldOps.Domain/WorkOrderDetails/WorkOrderComment.cs](../backend/src/FieldOps.Domain/WorkOrderDetails/WorkOrderComment.cs) · created
- [backend/src/FieldOps.Domain/WorkOrders/WorkOrder.cs](../backend/src/FieldOps.Domain/WorkOrders/WorkOrder.cs) · created
- [backend/src/FieldOps.Domain/WorkOrders/WorkOrderAssignment.cs](../backend/src/FieldOps.Domain/WorkOrders/WorkOrderAssignment.cs) · created
- [backend/src/FieldOps.Domain/WorkOrders/WorkOrderStatusHistory.cs](../backend/src/FieldOps.Domain/WorkOrders/WorkOrderStatusHistory.cs) · created

### Infrastructure and migration

- [backend/src/FieldOps.Infrastructure/Catalog/CatalogService.Reads.cs](../backend/src/FieldOps.Infrastructure/Catalog/CatalogService.Reads.cs) · created
- [backend/src/FieldOps.Infrastructure/Catalog/CatalogService.Writes.cs](../backend/src/FieldOps.Infrastructure/Catalog/CatalogService.Writes.cs) · created
- [backend/src/FieldOps.Infrastructure/Catalog/CatalogService.cs](../backend/src/FieldOps.Infrastructure/Catalog/CatalogService.cs) · created
- [backend/src/FieldOps.Infrastructure/Catalog/FieldOpsDbContext.Catalog.cs](../backend/src/FieldOps.Infrastructure/Catalog/FieldOpsDbContext.Catalog.cs) · created
- [backend/src/FieldOps.Infrastructure/DependencyInjection.cs](../backend/src/FieldOps.Infrastructure/DependencyInjection.cs) · updated
- [backend/src/FieldOps.Infrastructure/Operations/AuditLogReader.cs](../backend/src/FieldOps.Infrastructure/Operations/AuditLogReader.cs) · created
- [backend/src/FieldOps.Infrastructure/Operations/FieldOpsDbContext.Audit.cs](../backend/src/FieldOps.Infrastructure/Operations/FieldOpsDbContext.Audit.cs) · created
- [backend/src/FieldOps.Infrastructure/Operations/OperationalWriteContext.cs](../backend/src/FieldOps.Infrastructure/Operations/OperationalWriteContext.cs) · created
- [backend/src/FieldOps.Infrastructure/Persistence/FieldOpsDbContext.cs](../backend/src/FieldOps.Infrastructure/Persistence/FieldOpsDbContext.cs) · updated
- [backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261107100957_CoreOperations.Designer.cs](../backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261107100957_CoreOperations.Designer.cs) · created
- [backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261107100957_CoreOperations.cs](../backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261107100957_CoreOperations.cs) · created
- [backend/src/FieldOps.Infrastructure/Persistence/Migrations/FieldOpsDbContextModelSnapshot.cs](../backend/src/FieldOps.Infrastructure/Persistence/Migrations/FieldOpsDbContextModelSnapshot.cs) · updated
- [backend/src/FieldOps.Infrastructure/WorkOrderDetails/ChecklistTemplateService.cs](../backend/src/FieldOps.Infrastructure/WorkOrderDetails/ChecklistTemplateService.cs) · created
- [backend/src/FieldOps.Infrastructure/WorkOrderDetails/DetailRules.cs](../backend/src/FieldOps.Infrastructure/WorkOrderDetails/DetailRules.cs) · created
- [backend/src/FieldOps.Infrastructure/WorkOrderDetails/FieldOpsDbContext.WorkOrderDetails.cs](../backend/src/FieldOps.Infrastructure/WorkOrderDetails/FieldOpsDbContext.WorkOrderDetails.cs) · created
- [backend/src/FieldOps.Infrastructure/WorkOrderDetails/WorkOrderDetailService.cs](../backend/src/FieldOps.Infrastructure/WorkOrderDetails/WorkOrderDetailService.cs) · created
- [backend/src/FieldOps.Infrastructure/WorkOrders/FieldOpsDbContext.WorkOrders.cs](../backend/src/FieldOps.Infrastructure/WorkOrders/FieldOpsDbContext.WorkOrders.cs) · created
- [backend/src/FieldOps.Infrastructure/WorkOrders/WorkOrderAccess.cs](../backend/src/FieldOps.Infrastructure/WorkOrders/WorkOrderAccess.cs) · created
- [backend/src/FieldOps.Infrastructure/WorkOrders/WorkOrderService.cs](../backend/src/FieldOps.Infrastructure/WorkOrders/WorkOrderService.cs) · created

### API

- [backend/src/FieldOps.Api/Endpoints/AuditEndpoints.cs](../backend/src/FieldOps.Api/Endpoints/AuditEndpoints.cs) · created
- [backend/src/FieldOps.Api/Endpoints/CatalogEndpoints.cs](../backend/src/FieldOps.Api/Endpoints/CatalogEndpoints.cs) · created
- [backend/src/FieldOps.Api/Endpoints/WorkOrderDetailEndpoints.cs](../backend/src/FieldOps.Api/Endpoints/WorkOrderDetailEndpoints.cs) · created
- [backend/src/FieldOps.Api/Endpoints/WorkOrderEndpoints.cs](../backend/src/FieldOps.Api/Endpoints/WorkOrderEndpoints.cs) · created
- [backend/src/FieldOps.Api/Errors/GlobalExceptionHandler.cs](../backend/src/FieldOps.Api/Errors/GlobalExceptionHandler.cs) · updated
- [backend/src/FieldOps.Api/Program.cs](../backend/src/FieldOps.Api/Program.cs) · updated
- [backend/src/FieldOps.Api/Security/ApiSecurity.cs](../backend/src/FieldOps.Api/Security/ApiSecurity.cs) · updated

### Tests

- [backend/tests/FieldOps.IntegrationTests/CoreOperationsTestBase.cs](../backend/tests/FieldOps.IntegrationTests/CoreOperationsTestBase.cs) · created
- [backend/tests/FieldOps.IntegrationTests/CoreSecurityAndConcurrencyTests.cs](../backend/tests/FieldOps.IntegrationTests/CoreSecurityAndConcurrencyTests.cs) · created
- [backend/tests/FieldOps.IntegrationTests/OperationalApiContractTests.cs](../backend/tests/FieldOps.IntegrationTests/OperationalApiContractTests.cs) · created
- [backend/tests/FieldOps.IntegrationTests/OperationalAuditTests.cs](../backend/tests/FieldOps.IntegrationTests/OperationalAuditTests.cs) · created
- [backend/tests/FieldOps.IntegrationTests/PostgreSqlMigrationTests.cs](../backend/tests/FieldOps.IntegrationTests/PostgreSqlMigrationTests.cs) · updated
- [backend/tests/FieldOps.IntegrationTests/WorkOrderDetailSecurityTests.cs](../backend/tests/FieldOps.IntegrationTests/WorkOrderDetailSecurityTests.cs) · created
- [backend/tests/FieldOps.IntegrationTests/WorkOrderWorkflowTests.cs](../backend/tests/FieldOps.IntegrationTests/WorkOrderWorkflowTests.cs) · created
- [backend/tests/FieldOps.UnitTests/CatalogDomainTests.cs](../backend/tests/FieldOps.UnitTests/CatalogDomainTests.cs) · created
- [backend/tests/FieldOps.UnitTests/PaginationTests.cs](../backend/tests/FieldOps.UnitTests/PaginationTests.cs) · created
- [backend/tests/FieldOps.UnitTests/WorkOrderDetailDomainTests.cs](../backend/tests/FieldOps.UnitTests/WorkOrderDetailDomainTests.cs) · created
- [backend/tests/FieldOps.UnitTests/WorkOrderDomainTests.cs](../backend/tests/FieldOps.UnitTests/WorkOrderDomainTests.cs) · created

### Documentation

- [README.md](../README.md) · updated
- [docs/api.md](api.md) · updated
- [docs/architecture.md](architecture.md) · updated
- [docs/database.md](database.md) · updated
- [docs/implementation-status.md](implementation-status.md) · updated
- [docs/phase-2-migration-plan.md](phase-2-migration-plan.md) · created
- [docs/phase-2-results.md](phase-2-results.md) · created
- [docs/security.md](security.md) · updated
