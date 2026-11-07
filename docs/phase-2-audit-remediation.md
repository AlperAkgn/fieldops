# Phase 2 audit remediation report

2026-11-07 · Europe/Istanbul

## 1. Root cause

Scheduling and assignment audit recorded actions and override reasons without operational values. A reschedule within Assigned did not change status, so status history could not recover the prior interval either.

Before changing production code, a PostgreSQL regression reproduced the exact 10:00–11:00 to 09:30–10:30 override. It failed at the assertion that audit metadata must exist.

## 2. Implemented fix

Scheduling and assignment now read bounded database snapshots before mutation and after business saves, while holding the same organization lock and transaction. Typed metadata records previous and resulting entity versions and previous/current values for each changed planned instant, technician set or primary technician. Unchanged field entries are null. The snapshots use persisted PostgreSQL values and deterministic technician ordering.

Actor and timestamp remain in their existing audit fields. The existing override event retains its reason and carries the same metadata as the corresponding scheduling or assignment event. Their action names remain compatible. Metadata serializes only the allowlisted operational record. [Mapping approach](https://www.npgsql.org/efcore/mapping/json.html)

The final audit save and commit share the business transaction. Audit failure rolls back intermediate schedule, version and assignment saves. Stale-version rejection records no failed change. EF guards reject tracked audit edits/deletes, and a PostgreSQL trigger rejects UPDATE, DELETE and TRUNCATE.

For example, a reschedule that retains its assignments exposes this metadata.

```json
{
  "previousVersion": 3,
  "entityVersion": 4,
  "changes": {
    "plannedStartAt": {
      "previous": "2027-01-10T10:00:00+00:00",
      "current": "2027-01-10T09:30:00+00:00"
    },
    "plannedEndAt": {
      "previous": "2027-01-10T11:00:00+00:00",
      "current": "2027-01-10T10:30:00+00:00"
    },
    "technicianUserIds": null,
    "primaryTechnicianUserId": null
  }
}
```

## 3. Files changed

The source inventory below compares against the existing Phase 2 working tree at remediation start.

## 4. Database changes

`20261107124549_AuditOperationalChanges` adds nullable jsonb metadata with an object check and append-only database protection. It preserves all legacy audit fields and leaves their metadata null. Its downgrade refuses to remove captured history or protection.

Fresh PostgreSQL databases applied the complete migration chain. A populated CoreOperations schema upgraded successfully, retaining its legacy audit row and identity data. Typed metadata persisted and reloaded correctly, repeated migration application succeeded, and the model had no pending changes.

The default development database rollout remains separate. The new migration was applied only on isolated test databases.

## 5. New regression tests

Seven integration cases cover the exact Assigned reschedule and override, actor/time/version, technician replacement, primary-only changes, scheduling and assignment rollback when override audit fails, stale and forged requests, cross-tenant isolation, EF/SQL immutability, and populated schema upgrade. The unchanged status-history count is asserted for the original reschedule scenario.

## 6. Verification results

| Check | Observed result |
| --- | --- |
| Test-first reproduction | Failed with missing audit metadata before the production fix. |
| Focused audit verification | 10 cases passed, including three existing audit cases. |
| Locked restore, format and Release build | Passed with zero build warnings or errors. |
| Complete backend suite | 92 unit and 130 PostgreSQL integration cases passed, zero failures or skips. All 215 original cases remain, plus seven new cases. |
| Transaction, concurrency and isolation | The full run includes audit insert failure, multi-save rollback, stale versions, assignment/reschedule races and tenant isolation. |
| Docker/API | Linux image built and all six migrations applied on the isolated stack. Certificate-validated HTTPS smoke passed the exact reschedule/override metadata, actor and version checks, followed by the core workflow through completion and archive preservation. Mailpit delivered the local confirmation and invitation emails. |

## 7. Remaining issues

Earlier before/after values remain unavailable for legacy rows. Metadata is null for these records. Database schema-administrator privileges remain the boundary for altering the append-only trigger. Hosted CI execution requires its external runner.

## 8. Phase 3 readiness

**READY** for the verified local backend gate. The seven new regressions, complete 222-case backend run, populated migration upgrade and real Docker/API smoke passed. Phase 3 requires separate authorization.

## Source inventory

7 created files and 14 updated files in this remediation.

- [backend/src/FieldOps.Application/Operations/AuditContracts.cs](../backend/src/FieldOps.Application/Operations/AuditContracts.cs) · updated
- [backend/src/FieldOps.Domain/Operations/AuditLog.cs](../backend/src/FieldOps.Domain/Operations/AuditLog.cs) · updated
- [backend/src/FieldOps.Domain/Operations/WorkOrderAuditMetadata.cs](../backend/src/FieldOps.Domain/Operations/WorkOrderAuditMetadata.cs) · created
- [backend/src/FieldOps.Infrastructure/Operations/AuditLogReader.cs](../backend/src/FieldOps.Infrastructure/Operations/AuditLogReader.cs) · updated
- [backend/src/FieldOps.Infrastructure/Operations/FieldOpsDbContext.Audit.cs](../backend/src/FieldOps.Infrastructure/Operations/FieldOpsDbContext.Audit.cs) · updated
- [backend/src/FieldOps.Infrastructure/Operations/OperationalWriteContext.cs](../backend/src/FieldOps.Infrastructure/Operations/OperationalWriteContext.cs) · updated
- [backend/src/FieldOps.Infrastructure/Persistence/FieldOpsDbContext.cs](../backend/src/FieldOps.Infrastructure/Persistence/FieldOpsDbContext.cs) · updated
- [backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261107124549_AuditOperationalChanges.Designer.cs](../backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261107124549_AuditOperationalChanges.Designer.cs) · created
- [backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261107124549_AuditOperationalChanges.cs](../backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261107124549_AuditOperationalChanges.cs) · created
- [backend/src/FieldOps.Infrastructure/Persistence/Migrations/FieldOpsDbContextModelSnapshot.cs](../backend/src/FieldOps.Infrastructure/Persistence/Migrations/FieldOpsDbContextModelSnapshot.cs) · updated
- [backend/src/FieldOps.Infrastructure/WorkOrders/WorkOrderAuditSnapshot.cs](../backend/src/FieldOps.Infrastructure/WorkOrders/WorkOrderAuditSnapshot.cs) · created
- [backend/src/FieldOps.Infrastructure/WorkOrders/WorkOrderService.cs](../backend/src/FieldOps.Infrastructure/WorkOrders/WorkOrderService.cs) · updated
- [backend/tests/FieldOps.IntegrationTests/AuditHistoryRemediationTests.cs](../backend/tests/FieldOps.IntegrationTests/AuditHistoryRemediationTests.cs) · created
- [backend/tests/FieldOps.IntegrationTests/AuditMetadataMigrationTests.cs](../backend/tests/FieldOps.IntegrationTests/AuditMetadataMigrationTests.cs) · created
- [docs/api.md](api.md) · updated
- [docs/architecture.md](architecture.md) · updated
- [docs/database.md](database.md) · updated
- [docs/implementation-status.md](implementation-status.md) · updated
- [docs/phase-2-audit-remediation.md](phase-2-audit-remediation.md) · created
- [docs/phase-2-migration-plan.md](phase-2-migration-plan.md) · updated
- [docs/phase-2-results.md](phase-2-results.md) · updated
