# Phase 2 migration rollout plan

## Default development database inventory

The default Compose PostgreSQL database was queried read-only on 2026-11-07. Its migration history contained:

- `20261105132903_InitialFoundation`
- `20261105205852_IdentityAndOrganizations`

The Phase 1 security migrations `20261106170229_RefreshSessionLineage` and `20261106210629_RevokeUnactivatedAccountSessions` were pending. The database had zero users, organizations and active sessions when inspected. The existing API was healthy against its older image and schema. Recheck the history and counts in the maintenance window because local activity can change them.

## Safe application sequence

1. Confirm the default Compose project, PostgreSQL database and current migration history. Keep the default named volumes and Data Protection keys. Stop the existing API before applying the migrations so it cannot continue serving writes during the upgrade.
2. Create a PostgreSQL custom-format backup using `pg_dump -Fc`. Store it outside Git with restricted access. Preserve the API key ring and runtime secret configuration separately. Verify the archive using `pg_restore --list`, then restore into an empty disposable PostgreSQL database and rehearse the same upgrade there. [PostgreSQL backup documentation](https://www.postgresql.org/docs/18/app-pgdump.html)
3. From the verified source, review the forward migration SQL and build the API image. Apply the pending migrations in order through `docker compose run --rm migrate`. The first Phase 1 upgrade backfills token lineage and revokes sessions with ambiguous prior organization switches. The second revokes unactivated sessions. Both retain existing rows. The Phase 2 migration adds operational tables and constraints.
4. Verify the final migration history and absence of pending migrations. Compare counts and representative relationships with the rehearsal. Recreate the API with its configured development HTTPS certificate. Check liveness, readiness, sign-in and one tenant-scoped operational read.
5. If the upgrade fails, keep the old deployment stopped while diagnosing the failure. Prefer a forward fix. For recovery, restore the verified backup into a separate database, validate it, and then repoint the application. The Phase 1 security migrations deliberately reject downgrade. Preserve the original database and volumes during recovery.

Phase 2 development and tests use an isolated PostgreSQL instance. The default database upgrade is a separate rollout operation.

The audit remediation migration follows CoreOperations. It adds nullable structured metadata without backfilling earlier values, and installs append-only audit protection. Include this forward migration in the same isolated rehearsal and coordinated rollout.
