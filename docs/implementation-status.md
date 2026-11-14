# Implementation status

Last updated: 2026-11-12 (Europe/Istanbul).

## Authorization boundary

**Phases 0–3 are complete. Phase 4 online and Phase 5 offline implementations are available.** Phase 4 Android native validation is `DEFERRED / NOT VERIFIED` by owner decision. Phase 5 development was explicitly authorized with this gate deferred. Final mobile acceptance remains conditional on the physical-device runs. [Android validation handoff](phase-4-android-validation.md) · [Phase 5 report](phase-5-results.md)

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
| 3 | Complete; final focus fix verified | Successful-save dialog focus restoration corrected |
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

At the Phase 2 close, the default database retained its original two migrations. Phase 3 subsequently completed the backed-up, rehearsed forward rollout described below. Hosted CI, external SMTP and native devices remain separate verification environments. The organization write lock is deliberately coarse and should be measured before changing its scope.

Phase 3 was subsequently authorized and implemented against these APIs. [Phase 2 results and source inventory](phase-2-results.md) · [Migration rollout plan](phase-2-migration-plan.md)

## Phase 3 — Admin web dashboard

### Final accessibility fix (current evidence, 2026-11-08)

- Independent re-audit confirmed five resolved findings and one remaining Edit → successful Save → BODY focus defect. A new actual-browser regression failed first after PATCH 200/dialog close because Edit was not focused.
- Root cause: successful-close callbacks run while the mutation is still pending, so the connected Edit trigger remains disabled during modal cleanup. Focus now falls back safely to main when restoration cannot succeed; the Edit owner restores the captured trigger in a layout effect after the mutation/refetch idle state and enabled DOM attribute have committed. No timers, observer workaround, mutation-rule weakening or component restructuring was introduced.
- Only production files `components/ui.tsx` and `pages/WorkOrdersPage.tsx` changed for this fix. Added two `Modal.test.tsx` cases and one real API browser scenario. API errors preserve the modal/input; Escape, Tab/Shift+Tab, disabled-trigger state and actual trigger-unmount/main fallback all pass.
- Verification: **58/58 web tests** (all existing 56 + 2 new), lint, strict typecheck, Prettier and production build pass. **12/12 Playwright E2E scenarios** (all existing 11 + the new focus regression) pass in one complete run, one worker, **zero skipped/retries**, about **2.8 minutes**, against real HTTPS/PostgreSQL/Mailpit on local Edge.
- Backend code, authentication, concurrency, schemas and dependencies were untouched. The 227 backend tests were intentionally not rerun as requested. No user data was deleted. The existing approximately 503 kB main-bundle advisory and hosted/native/other-browser verification limits remain documented.
- **Phase 4 readiness: READY for independent revalidation and explicit implementation authorization.** Stop here. [Focused accessibility report and changed files](phase-3-final-accessibility-fix.md)

### Earlier quality-gate remediation (historical evidence)

- Independent audit identified **four Medium (M1 version coordination, M2 mobile focus, M3 contrast, M4 invitation paging) and two Low (L1 modal positioning, L2 archived-template controls)** findings. All six were corrected with one agent; the earlier local implementation-only readiness claim is superseded.
- Test-first evidence: a delayed checklist/detail refetch regression failed because Start remained enabled against the old version. The 2026-11-08 continuation preserved the existing six fixes and confirmed that regression fails with a source-external legacy-behavior adapter, then passes with the current implementation. Shared aggregate mutation state/locking, acknowledged versions, selective reconciliation and pending guards synchronize subsequent writes. Two additional tests verify failed reconciliation without write replay and lock ownership through refetch. Frozen snapshots and explicit reload preserve genuine concurrency errors; no backend rule was relaxed.
- Offset/limit member and invitation lists now use one lookahead row without fabricated totals and retain Previous on empty pages. Archived templates are read-only. Native dialogs are centered, trap focus, restore focus/scroll and expose long content. Closed mobile navigation is inert/hidden to assistive technology and responds correctly to keyboard/breakpoint changes. The light caption contrast is **5.150:1**, versus the former 4.079:1; measured light/dark normal-text samples pass 4.5:1.
- Final verification (2026-11-08): web lint, strict typecheck, Prettier and production build pass; **56/56 web tests** (original 40 + 16 regression cases) pass. **11/11 real HTTPS/PostgreSQL/Mailpit Playwright scenarios** (all original 8 + 3 new) pass in one complete run, **zero skipped/retries**, one worker, about **2.6 minutes**. Each scenario has an independent organization, fresh sessions and operational fixtures; email matching prevents old invitation reuse. Responsive/modal/focus/contrast checks pass at **375, 768 and 1440 px**. Screenshots now use per-test output paths, preserving earlier evidence.
- Backend Release build **zero warnings/errors**, format pass, **227/227 tests (92 unit + 135 real PostgreSQL integration), zero skipped**, confirmed in one full invocation. Normal-limit isolated Docker trusted HTTPS/CORS/CSRF/SMTP/refresh/logout/429 smoke passes. Current logs, TRX and HTML browser evidence are under ignored `artifacts/phase-3-remediation-20261108`. The default database retained **0 users / 0 organizations / 6 migrations**; no user volumes were deleted. Backend sources, schemas and dependencies were unchanged.
- Remaining limits: Vite reports the approximately 502 kB main bundle advisory (153 kB gzip); no threshold was raised. Hosted CI, other browser engines, native devices, production and external SMTP remain unverified environments. Existing dependency-advisory history is unchanged; no new audit is claimed. No unresolved finding from M1–M4/L1–L2 remains in the tested scope.
- **Phase 4 readiness: READY for independent revalidation and explicit implementation authorization.** Stop before Phase 4. [Eleven-section remediation report](phase-3-quality-gate-remediation.md)

### Original Phase 3 implementation evidence (historical)

- Completed: real login/registration/activation/recovery, secure sessions and workspace switching; responsive navigation/theme/accessibility; customer/location/asset/category CRUD/archive and completed service history; versioned work-order create/edit/schedule/assign/status, comments, checklists/templates and structured audit history; daily/weekly calendar and reasoned conflict override; owner team/invitation/profile administration, restricted manager technician lookup, reports and settings.
- Architecture: existing modular monolith and authentication/write boundaries preserved. Added only policy/tenant-scoped read-only report and technician queries. Reports use one PostgreSQL repeatable-read snapshot. Access tokens stay in memory; existing Secure HttpOnly refresh cookies, identity-bound CSRF and Origin checks remain authoritative. Queues/Web Locks serialize rotation; cross-tab broadcasts carry no credentials. Cache keys include tenant/user/role/session, and late tenant responses are rejected.
- Database: backed up the default database and Data Protection keys; restored the dump into a separate PostgreSQL container; verified upgrade and clean-schema installation. Applied the four pending Phase 1/2 migrations safely with the old API stopped, preserved all development volumes, verified six migration entries and unchanged zero user/organization counts, then passed idempotent reapplication and HTTP/trusted HTTPS health. No new Phase 3 migration was required. [Rollout evidence](phase-3-migration-plan.md)
- Verification: backend Release build zero warnings/errors and format pass; **227 backend tests passed (92 unit + 135 actual PostgreSQL integration), zero skipped**. All original 222 tests remain. Web lint, strict typecheck, production build and Prettier check pass; **40 web tests passed**. **8 real Playwright E2E scenarios passed**, one worker, zero retries, on local Edge: manager CRUD/assignment/required checklist/completion, override/audit values, SMTP-backed registration/recovery, workspace/cache switching/logout, viewer UI/API denial, responsive/dark/keyboard behavior, owner administration and two-tab rotating-cookie/session coordination. Mobile lint/typecheck, **23 tests**, and Expo dependency compatibility pass. Docker build/Compose/health and actionlint pass locally.
- Limits: hosted CI, external SMTP and native device execution were not run. Downloaded Chromium failed to spawn on this Windows host; installed Edge executed the full browser suite. Admin web runtime dependency audit has zero findings; the existing Expo CLI/glob/minimatch chain has five moderate runtime-audit findings. Profile fields are read-only apart from existing password/session controls. Calendar times use the labeled browser zone; bounded reports are documented. No public deployment or unsupported feature is claimed.
- Pending: no Phase 3 functional blocker found in the implemented scope. Rich analytics, drag/drop, realtime, notifications and attachments remain later work. Full release hardening and other browser/native environments retain their separate gates.
- Next steps: stop and await explicit Phase 4 authorization; then implement the supported online technician flow and verify it on a native target. **Phase 4 readiness: READY for implementation.** [Detailed Phase 3 report](phase-3-results.md) · [Changed files](phase-3-files.md)

## Phase 4 — Technician mobile app

- Completed: Expo Router protected sign-in/tabs/detail; SecureStore refresh-only persistence, memory access credentials, serialized/deduplicated rotation with interruption markers, workspace selection and cache/late-response isolation; technician Today/assigned-task search/status/priority/pagination/pull-refresh; customer/site/contact/asset/team details and maps; versioned checklist, service notes, start/pause/resume/complete, server actual times/history; profile, recovery request and logout. No backend/API/schema/security contract was changed. [Mobile setup and architecture](mobile.md)
- Verification: clean `npm ci` and complete peer tree; mobile lint, strict typecheck, Prettier, **55 Vitest tests (all 23 original health tests retained) + 13 native-component tests**; **7 real HTTPS API/PostgreSQL mobile integration tests**, zero skipped. Expo compatibility and Doctor **20/20**, Android/iOS Hermes JavaScript export, Metro startup/Android manifest/HTTP-bundle smoke, Android prebuild and static backup/permission configuration checks passed. Backend Release build **zero warnings/errors**, **227 tests (92 unit + 135 PostgreSQL integration)** passed; web lint/typecheck/build and **58 existing tests** passed; actionlint and Git whitespace checks passed. Logout-interruption and stale-401/workspace races were reproduced with failing tests and fixed before final regression. [Full results and commands](phase-4-results.md)
- Database safety: fresh `fieldops-phase4` Compose project on isolated ports; the existing migration chain applied there without new migrations. Backend tests used generated disposable databases on that isolated PostgreSQL server. The default development database was not migrated, seeded or deleted; its readiness still reports Healthy. An initial backend test attempt was interrupted after IPv6 `localhost` connection stalls; the complete rerun with `Host=127.0.0.1` passed in about 64 seconds for the integration assembly.
- Android follow-up on 2026-11-12: prepared the EAS `android-validation` and `development` APK profiles, SDK 58 patch alignment, archive rules and HTTPS staging/LAN options. Local EAS profile and archive checks, locked install and peer tree, Expo compatibility, Android prebuild, Hermes export, TLS checks and 55 Vitest plus 13 component tests succeeded. [Preparation evidence](phase-4-android-validation.md#preparation-evidence-from-2026-11-12)
- Native validation: `DEFERRED / NOT VERIFIED`. Cloud build, device TLS, APK installation and the 14 Galaxy A34 checks remain open. Local SDK/ADB are absent. Phase 5 verified a separate Compose HTTPS stack from the host. Phone-to-LAN/staging reachability remains a device check.
- Remaining: complete the documented device acceptance flow and independent review. Root dependency audit reports **33 advisories (32 moderate, 1 low; zero high/critical)** through the build/test toolchain (`glob`, `minimatch`, `deepmerge` and dependent packages); no compatible forced SDK/toolchain downgrade was applied. The pre-existing optional Ajv peer mismatch was fixed with a pinned compatible web development dependency; the complete peer tree passes. The prior web approximately 503 kB bundle advisory remains visible.
- Next steps: when scheduled, follow the [Android validation handoff](phase-4-android-validation.md) and record the actual device results. Use a new SQLCipher-enabled native build if validating both phases together. [Original Phase 4 report](phase-4-results.md) · [Original changed files](phase-4-files.md)

## Phase 5 — Offline-first system

- Completed: scoped SQLite cache and ordered outbox, connection and lifecycle integration, retry/backoff, technician sync endpoints, transactional receipts and committed-order cursors, resnapshot-based removal, version conflicts and interactive Sync center. Supports downloaded assignments, checklist changes, service notes and technician lifecycle commands. The local lease is 24 hours. The server replay/cursor window is seven days and receipts are retained without pruning. [Offline protocol and limits](offline-sync.md)
- Database: migration `20261113203829_OfflineSynchronization` was applied in isolated PostgreSQL environments. The default development database and existing Docker volumes were preserved.
- Verification: Release build and .NET format passed. **232 backend tests** (92 unit, 140 PostgreSQL), **64 mobile Vitest + 14 React Native component + 8 HTTPS API/PostgreSQL integration tests**, **58 web tests + 12 Edge Playwright scenarios** passed. Strict checks, Expo compatibility, Android/iOS JavaScript export, Metro smoke, SQLCipher config introspection and Compose HTTPS readiness passed.
- Pending native acceptance: `DEFERRED / NOT VERIFIED`. Runtime cipher, physical encryption/backup, process restart, radio transitions, workspace/logout/revocation cleanup and interaction accessibility require the new native binary on a supported device.
- Next steps: **READY for independent Quality Gate**. **Phase 6 readiness: CONDITIONAL** on the outstanding physical-device runs for final mobile acceptance. Phase 6 remains a separate authorization. [Verification and readiness](phase-5-results.md#9-regression-results)

## Phase 6 — Advanced features

- Completed: local MinIO infrastructure; basic daily/weekly calendar, conflict/audit UI and bounded reports were explicitly delivered in Phase 3. The Phase 6 milestone itself has not started.
- Pending: tenant/resource-scoped SignalR, transactional delivery, in-app/push notifications/device lifecycle, safe attachments/camera/gallery, richer scheduling/drag-and-drop and deeper analytics.
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
