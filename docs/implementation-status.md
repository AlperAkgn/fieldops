# Implementation status

Last updated: 2026-11-07 (Europe/Istanbul).

## Authorization boundary

**Phases 0–2 are complete. Phase 2 audit remediation passed the local quality gate. Phases 3–8 have not started.** Phase 3 requires separate explicit authorization.

## Phase 0 revalidation (historical)

The Phase 0 audit recorded readiness for Phase 1. The Phase 1 section below contains the current implementation and test evidence.

The revalidation superseded the historical first-run table below. The audit records the fourteen checks, fixes, environmental blocks and maintenance items. [Phase 0 validation](phase-0-validation.md)

- Windows and clean Linux: Release build/format, actual PostgreSQL tests **21/21** (6 unit + 15 integration), both client lint/typechecks, web build and client tests **23 + 23**. **67 distinct tests passed**, zero skipped.
- Expo: clean lockfile installation (670 packages), valid peer tree, strict negative compiler probes, Doctor **20/20**, Android/iOS JavaScript export, and real Metro startup/Android manifest/HTTP bundle smoke all passed. Native execution is not claimed.
- Docker: API and MinIO no-cache builds, isolated fresh Compose bootstrap, migration/reapply, real HTTP health/CORS/OpenAPI, signed MinIO write/read, non-root execution and current .NET 10.0.14 runtime passed. The default development stack was updated and is healthy; temporary test volumes were removed.
- Fixed malformed/contradictory health response handling, unsafe API URL display, npm argument forwarding, actual Expo workspace startup resolution, backend correlation/error redaction, empty OpenAPI paths, unbounded PostgreSQL health handshake, development CORS, outdated container runtime, and missing API container healthcheck. CI now also checks live Metro and full Docker infrastructure.
- Workflow actionlint and official SHA metadata checks pass; equivalent build/test commands passed locally on Linux. Hosted CI cannot be marked passed without a remote/run.
- Blocked: hosted CI, native device/package execution, and an additional container interruption/restart-persistence command rejected by automatic approval review (`blocked by policy`). That command did not execute and is not a passed test.
- Remaining: **14 moderate npm tooling findings, 0 high/critical**, archived local-only MinIO, and an older installed Windows runtime (the verified Docker/CI runtime/toolchain is current). See the validation report for exact scope and evidence.
- Next action: stop and wait for explicit Phase 1 authorization.

| Phase | Status | Scope |
| --- | --- | --- |
| 0 | Complete | Foundation and architecture |
| 1 | Complete | Identity and multi-tenancy |
| 2 | Complete | Core domain and API |
| 3 | Not Started | Admin web dashboard |
| 4 | Not Started | Technician mobile workflows |
| 5 | Not Started | Offline-first synchronization |
| 6 | Not Started | Real-time, notifications, attachments, scheduling, analytics |
| 7 | Not Started | Quality and hardening |
| 8 | Not Started | Portfolio and release preparation |

## Phase 0 — Foundation and architecture

### Completed tasks

- Inspected the empty workspace: no existing source, parent AGENTS rules, or Git repository. Initialized local Git without creating a remote or publishing.
- Checked tools and started the installed Docker Desktop Linux engine: .NET SDK 10.0.401/runtime 10.0.12, Node 24.21.0, npm 11.19.0, Docker Engine 29.8.0.
- Analyzed the complete requirements and all nine phases. Wrote substantive product, architecture, database/ERD, API, security, offline-sync, deployment, roadmap, and ADR documents, plus AGENTS engineering/resume rules.
- Created the four-project .NET solution, strict build properties, locked NuGet dependencies, and inward dependency checks. Domain/Application deliberately contain no speculative feature code.
- Added Npgsql/EF Core persistence, an intentionally empty `InitialFoundation` migration, and an explicit `--migrate` operation. Normal API startup does not alter the schema.
- Added liveness, PostgreSQL plus migration readiness, safe ProblemDetails, structured JSON logs, correlation IDs, explicit CORS, and development OpenAPI.
- Created npm workspace applications: strict TypeScript React/Vite and Expo Router foundations. Both read actual health endpoints and support loading, errors, cancellation, timeouts, and retry. English message dictionaries establish the localization boundary.
- Added PostgreSQL, API, one-shot migration, and local source-built MinIO Compose services. API/MinIO run as non-root users. Local credentials are ignored by Git; `.env.example` documents setup.
- Added SHA-pinned GitHub Actions: locked restores, backend build/format/PostgreSQL tests, client lint/typechecks/tests, web build, Expo compatibility and Android/iOS JavaScript export.
- Wrote README/setup instructions and the complete [created-file manifest](phase-0-files.md).

### Initial implementation evidence (historical)

The first-run checks below were performed locally on 2026-11-05. The Phase 0 revalidation superseded them. See the Phase 1 section for the current test counts.

| Check / command | Actual result |
| --- | --- |
| `dotnet tool restore` | Passed |
| `dotnet restore backend/FieldOps.slnx --locked-mode` | Passed; also passed in the Linux Docker build |
| `dotnet build backend/FieldOps.slnx --configuration Release --no-restore` | Passed: 0 warnings, 0 errors |
| `dotnet format backend/FieldOps.slnx --verify-no-changes --no-restore` | Passed |
| Full `dotnet test backend/FieldOps.slnx --configuration Release` with `FIELDOPS_TEST_DATABASE` | 6 unit + 8 integration tests passed; 14 total, 0 skipped |
| Real PostgreSQL integration test | Fresh database not ready before migration; ready afterward; second migration idempotent; no application entities; test database cleaned up |
| `npm ci --no-fund --no-audit` | Passed: 673 packages reconstructed from lockfile |
| `npm run web:lint` / `web:typecheck` / `web:build` | All passed |
| `npm run web:test` | 7 passed |
| `npm run mobile:lint` / `mobile:typecheck` | Both passed |
| `npm run mobile:test` | 7 passed |
| `npm run mobile:check` | Passed: Expo dependency versions aligned |
| `npx expo-doctor@1.21.6` | 20 of 20 checks passed |
| `npm run mobile:export` | Android and iOS Hermes bundles exported |
| `docker compose config --quiet` | Passed |
| `docker compose up -d --build --wait` | Passed: PostgreSQL/MinIO healthy, API running, migration exited 0 |
| `GET http://localhost:5080/health/live` | HTTP 200: `{"status":"Healthy","checks":[]}` |
| `GET http://localhost:5080/health/ready` | HTTP 200: PostgreSQL `Healthy`; correlation header present |
| `GET http://localhost:5080/openapi/v1.json` | HTTP 200, OpenAPI 3.1.1 in Development |
| `GET http://localhost:9000/minio/health/ready` | HTTP 200 |
| Host `dotnet ef database update --project backend/src/FieldOps.Infrastructure --startup-project backend/src/FieldOps.Api --configuration Release --no-build` | Passed on the migrated Compose database (`Done.`) |
| PostgreSQL actual table inspection | Only `public.__EFMigrationsHistory`; no Phase 1 tables |
| Browser smoke at `http://localhost:5173` | Both real health cards `Healthy`; Check connection exercised; rendered screen visually inspected |
| Source hygiene | `.env`, node_modules, native bundles and .NET bin/obj excluded from Git |

The **28 passing tests** cover layer dependencies, error/health redaction, CORS allow/deny, probes, development-only OpenAPI, missing-route ProblemDetails, real migration/readiness, and each client's health transport/validation/cancellation/timeouts. Product identity, tenant, work-order, offline, and E2E tests are not claimed.

One intermediate Release build overlapped an active testhost and hit a Windows file lock. Checks were serialized; the final Release build passed with zero warnings/errors. No check was disabled.

### Architecture decisions

- Modular monolith with Domain/Application/Infrastructure/API; no microservices or generic repository.
- Shared database/schema tenancy is designed for Phase 1 onward: validated membership, scoped reads/guarded writes, tenant-composite relationships, backend policies, and PostgreSQL isolation tests. An empty baseline does not implement tenant isolation.
- Expo-supported React/native dependency matrix and strict npm workspace clients. TypeScript 6.0.3 matches the Expo template and supported lint parser; incompatible compiler versions were not forced.
- Separate schema migration job; infrastructure-only baseline keeps later-phase entities out of Phase 0.
- Feature libraries are introduced with real use cases in their owning phases.
- Local MinIO is built from a pinned final source-only security release. Its archived upstream is an explicit local-only exception; reassess before Phase 6. See [ADR 0002](adr/0002-local-object-storage.md).

### Known limitations and blockers

- Phase 0 passed its foundation gate. Identity and tenant enforcement were delivered in Phase 1. Operational workflows, offline persistence, notifications, uploads and reports are later phases.
- The Phase 0 run covered local verification. Hosted CI, native device execution, native build/signing and cloud deployment were outside the observed runs.
- `npm audit`: **14 moderate findings, 0 high, 0 critical**, propagated from tooling dependencies `minimatch` 10.2.6 ([GHSA-g3hv-75wg-m249](https://github.com/advisories/GHSA-g3hv-75wg-m249)) and `glob` 13.0.6 ([GHSA-3g3m-2fjr-6pw4](https://github.com/advisories/GHSA-3g3m-2fjr-6pw4)). Expo/ESLint tools constrain these major lines. Force-fix proposed incompatible downgrades; no unsupported override was applied. Track compatible upstream fixes during dependency updates and Phase 7. These are two underlying advisories, not 14 distinct issues.
- MinIO's upstream is archived; its pinned source build does not establish that transitive dependencies remain maintained. It is local-only.
- Compose uses local HTTP and a schema-capable development database account. Shared/staging/production TLS, least-privilege roles, secrets, backup/restore, and provider/signing credentials remain future work.

### Pending tasks and next steps

The Phase 0 work is complete. The following section records the separately authorized Phase 1 work.

## Phase 1 — Identity and multi-tenancy

### Completed tasks

- Added Identity registration and login, JWT access, current-user lookup, email confirmation, password recovery and account deactivation.
- Added native and web session transport, refresh rotation with replay revocation, logout and session management. Organization switching requires the current refresh credential from the bearer session.
- Added organization creation, selection and settings, membership roles and status, expiring invitations, resource authorization and transactional last-owner protection.
- Added migration `20261105205852_IdentityAndOrganizations`, membership-derived tenant context, EF filters and write guards, and composite tenant foreign keys.
- Added the encrypted SMTP outbox, local Mailpit, deployment configuration and CI coverage. Updated setup, API, security and database documentation.

### Initial verification results (historical)

- Release build and format passed with zero warnings and errors. Locked NuGet restore with full audit passed without advisory warnings.
- All 98 backend tests passed, comprising 24 unit and 74 integration tests with zero skips. Database cases used real PostgreSQL 18.6 and include replay, CSRF, role and tenant isolation, concurrent membership operations and database constraints.
- Both migrations applied on PostgreSQL. EF reported no pending model changes and migration reapplication succeeded.
- Web and mobile lint and strict typechecking passed. All 46 client tests passed. Web production build, Expo dependency validation, Android/iOS export and Android Metro HTTP smoke passed.
- Local Compose, HTTP health, authentication and SMTP flows passed. CI workflow syntax passed `actionlint`. [Phase 1 verification and source inventory](phase-1-results.md)

### Initial security remediation (historical)

- Preserved the refresh token family across organization switches. Replay revokes descendant access and refresh credentials. PostgreSQL constraints protect lineage integrity.
- Configured HTTPS for the web development server and Compose API. HTTP web authentication returns safe ProblemDetails. Added a trusted HTTPS smoke test to the Compose CI job.
- Applied IP quotas before JWT and database session validation. Retained authenticated user quotas for organization and invitation routes.
- Changed registration to a generic 202 with password setup after mailbox proof. Added control-character validation, route-specific OpenAPI errors and bounded owner lists.
- Verified 24 unit and 88 integration tests, zero failures or skips. Real PostgreSQL tests covered fresh and populated migrations, tenant isolation, replay, races and selective legacy session revocation. EF reported no pending model changes.
- Verified Docker HTTPS, CORS, CSRF, real local SMTP, activation, login, refresh, logout and signed-request throttling. Both clients passed lint, strict typecheck and 46 tests in total. Web build, Expo exports and Metro smoke passed.

The initial gate recorded READY. The subsequent independent re-audit reproduced two high-severity issues and one medium-severity issue. [Initial remediation results](phase-1-remediation.md)

### Final security remediation and quality gate

- Selection on both transports now verifies the exact signed session and refresh credential in the user-locked transaction. Replay through either selection route revokes the active family, including the winner of a concurrent switch, while separate sign-ins retain their own families.
- Login, access validation, refresh and selection require an active account with confirmed email and a password hash. The additive `20261106210629_RevokeUnactivatedAccountSessions` migration revokes legacy sessions for incomplete activation while preserving account, membership and token history.
- Native and web login reject control characters in email with 400 before database access. Existing Unicode email login remains supported.
- All original 112 backend tests were retained. The final Release run passed **127 tests** on PostgreSQL 18.6, comprising **24 unit and 103 integration cases**, with zero failures or skips. The legacy invitation test now expects 401 at authentication and still exercises verified acceptance and replay rejection.
- Locked restore, Release build, format verification, migration model verification, 46 client tests, client lint and typechecks, the web build, Expo exports and Metro smoke passed. The isolated Docker stack applied all four migrations and passed trusted HTTPS and Mailpit authentication smoke.

**Phase 2 readiness is READY.** The default development database was preserved. [Final remediation report, verification and rollout limits](phase-1-final-remediation.md)

### Pending tasks and blockers

The Phase 1 acceptance tasks are complete. External SMTP delivery, hosted CI and native device execution were outside the observed runs. Production throttling and session retention remain hardening tasks.

### Next steps

Phase 2 used these identity and tenant boundaries for the core operational APIs.

## Phase 2 — Core domain and API

### Completed tasks

- Implemented customer, location, asset category and asset management with validation, partial updates, archive and stable filtered pagination.
- Implemented work orders, schedules, multiple technician assignments and a primary technician, availability conflicts and audited overrides, exact state transitions, actual timestamps and status history.
- Implemented comments with internal visibility restrictions, reusable checklist templates and immutable snapshots, progress and required-item completion rules.
- Added transactional business audit, resource authorization, tenant-composite foreign keys and bigint concurrency versions. Writes serialize on the organization row and revalidate current access.
- Added `20261107100957_CoreOperations` and updated architecture, API, security, database and setup documentation. The continuation was completed by the main agent.

### Verification results

- Locked restore, Release build and format passed with zero build warnings and errors.
- 92 unit cases passed. The full PostgreSQL integration suite passed all 123 cases, including Phase 0 and Phase 1 regressions, with zero failures or skips.
- Verified Manager and Technician lifecycles, all 49 status pairs, checklist snapshot preservation, required-item gates, comment visibility, cross-tenant and same-customer foreign keys, write guards, concurrent version/assignment/reschedule conflicts, role changes while waiting on locks, audit rollback and guarded migration downgrade.
- The isolated Docker image applied all five migrations and passed trusted HTTPS core API and web authentication smoke tests with actual local Mailpit delivery. EF reported no pending model changes and repeated migration application succeeded.
- Client lint, strict typechecks, 46 client tests, web build, Expo compatibility, Android/iOS exports and live Metro smoke passed during this Phase 2 task. Workflow syntax passed `actionlint`.

### Audit history remediation

- Reproduced the independent audit finding with a failing test before changing production code.
- Added allowlisted before/after schedule, assignment and primary metadata with entity versions, using PostgreSQL snapshots under the existing organization lock. Actor, time and override reason retain their existing audit fields.
- Added EF and PostgreSQL append-only protection and the nullable `20261107124549_AuditOperationalChanges` migration. Legacy audit values remain intact and their missing metadata remains null.
- Passed 10 focused audit cases, then all 92 unit and 130 PostgreSQL integration cases with zero failures or skips. All original 215 backend cases remain, with seven new integration cases.
- Release build and format passed with zero build warnings and errors. Fresh and populated upgrades, typed metadata round-trip, guarded downgrade, transaction rollback, stale versions, tenant isolation and immutable history passed.
- The isolated Linux image applied all six migrations and passed trusted HTTPS audit and core API smoke. EF reported no pending model changes and migration reapplication succeeded.

**Phase 3 readiness is READY.** [Audit remediation evidence and changed files](phase-2-audit-remediation.md)

### Pending tasks, limits and next steps

The default database retains its original two migrations and requires the documented backup and rollout procedure. Hosted CI, external SMTP and native devices remain separate verification environments. The organization write lock is deliberately coarse and should be measured before changing its scope.

After explicit Phase 3 authorization, build the admin web workflows against these APIs. [Phase 2 results and source inventory](phase-2-results.md) · [Migration rollout plan](phase-2-migration-plan.md)

## Phase 3 — Admin web dashboard

- Completed: no Phase 3 feature; Phase 0 health screen only.
- Pending: authentication/protected routing, responsive navigation, real dashboard and customer/location/asset/work-order/team workflows, assignments/details, forms and loading/error/empty/confirmation states.
- Blockers: authenticated core APIs and separate user authorization.
- Verification: product UI/E2E not run. Plan component/form/error/protection checks and Playwright manager login→customer→location→work order→assignment against actual APIs.
- Next steps: build real workflows after their backend contracts pass acceptance.

## Phase 4 — Technician mobile app

- Completed: no Phase 4 feature; Expo Router/health foundation only.
- Pending: authentication/SecureStore, assigned tasks/details, map linking, checklist/notes/time/status actions, API/loading/errors and logout cleanup.
- Blockers: backend workflows, separate user authorization and supported native test target.
- Verification: technician flows not run. Plan session/transport/component tests and an online technician lifecycle on device/emulator.
- Next steps: finish the online flow before offline mutations.

## Phase 5 — Offline-first system

- Completed: design only; no offline implementation.
- Pending: SQLite/cache/outbox, local atomic updates, sync receipts/idempotency, versions/conflicts, committed-order authorized pull cursors, tombstones/revocations, retry/recovery, offline indicator and sync center.
- Blockers: online technician flow, replay/retention decisions, native persistence/encryption verification and separate authorization.
- Verification: not run. Plan duplicates, interruptions/restarts, retry, delayed commits, conflicts, authorization changes, protocol/version mismatch and failure recovery.
- Next steps: implement and verify the local/server protocol before enabling offline claims.

## Phase 6 — Advanced features

- Completed: local MinIO infrastructure only; no Phase 6 feature.
- Pending: tenant/resource-scoped SignalR, transactional delivery, in-app/push notifications/device lifecycle, safe attachments/camera/gallery, calendar/history/conflicts and actual analytics.
- Blockers: core workflows, separate authorization, maintained storage reassessment and native push credentials for delivery acceptance.
- Verification: not run. Plan recipient isolation/retries, safe upload/download, concurrent/DST schedule conflicts and database-backed statistics.
- Next steps: reassess storage and implement each feature with its real client workflow.

## Phase 7 — Quality and hardening

- Completed: foundation checks only; no product hardening milestone.
- Pending: security/tenant/IDOR review, dependency advisories, measured performance/query/index work, OpenTelemetry, error/recovery/accessibility expansion, production separation and operational/backup review.
- Blockers: product workflows to review/measure and separate authorization.
- Verification: not run. Plan adversarial security, recovery/E2E/accessibility tests and representative performance evidence.
- Next steps: resolve measured risks and document remaining release limitations.

## Phase 8 — Portfolio and release preparation

- Completed: foundation README/design/setup only; no release milestone.
- Pending: final README/architecture/ERD/API polish, real screenshots, development-only synthetic demo seed, clean-checkout demo, deployment/staging guide, hosted CI review, final acceptance/release checklist.
- Blockers: accepted product/hardening and separate authorization; external deployment/signing/email/push credentials as required.
- Verification: not run. Plan clean setup/migration/seed, real manager/technician workflows, documentation accuracy and release/restore checks.
- Next steps: prepare a reviewable release only after prior authorized gates pass.
