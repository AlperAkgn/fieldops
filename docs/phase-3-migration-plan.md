# Phase 3 safe development database rollout

The Phase 3 start inspection found only `InitialFoundation` and `IdentityAndOrganizations` applied in the default Compose database. Four existing migrations were pending: `RefreshSessionLineage`, `RevokeUnactivatedAccountSessions`, `CoreOperations`, and `AuditOperationalChanges`. Phase 3 adds read-only query services and needs no new schema migration.

## Backup and rehearsal

1. Read the migration history and user/organization counts without changing the database. The default database contained zero users and zero organizations.
2. Create a PostgreSQL custom-format `pg_dump` inside the database container and copy the binary archive to the ignored `artifacts/phase-3/before-phase3.dump`. Preserve the API's Data Protection key directory separately in the same ignored folder. Both backups contain private material and must remain outside Git.
3. Verify the archive manifest with `pg_restore --list`. Restore into a new `fieldops_rehearsal` database in the separate `fieldops-phase3` PostgreSQL container on port 5549. Do not use `--clean` or delete any development volume.
4. Run the existing API's explicit `--migrate` entry point against the restored database. Check six migration history entries, zero users and zero organizations. This rehearsal passed on 2026-11-07. A second, empty database was also migrated for real browser tests.

## Forward rollout

Build the current API image, stop the old API to prevent writes during the upgrade, and run the explicit migration service against the default database. Preserve all existing volumes and keys. Start the updated API with the localhost development TLS certificate; verify live/ready over HTTP and HTTPS, migration count, unchanged user/organization counts and idempotent migration reapplication. Record actual results in the implementation report.

If the migration fails, keep the API stopped, retain logs and the original volumes, and inspect the failure. Restore the archive into a separate recovery database before considering any destructive replacement. Do not automatically downgrade or overwrite the default database. For nonempty environments the session-revocation migration has a deliberate security impact that must be scheduled and communicated.

E2E accounts and operational records belong only to the isolated `fieldops-phase3` stack. The default development database is not a browser-test seed target.
