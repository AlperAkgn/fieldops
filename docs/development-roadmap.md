# Development roadmap

## Working agreement

There are exactly nine phases, numbered 0–8. Analyze the complete product now, implement only the phase authorized by the user, verify it, update [implementation-status.md](implementation-status.md), and stop. **Phase 1 starts only after the user says to proceed.** This gate applies to every subsequent phase and overrides the master prompt's automatic continuation sentence.

Each phase produces working code, documentation, and relevant evidence. A failed or unavailable acceptance check is recorded honestly; the phase is not silently declared fully verified. Preserve existing code and follow vertical slices: domain rule, mapping/migration, use case, HTTP/authorization, tests, then real UI integration. Security and tests begin with the feature; Phase 7 expands them rather than postponing them.

| Phase | Name | Dependencies |
| --- | --- | --- |
| 0 | Foundation & Architecture | Empty-repository/toolchain inspection |
| 1 | Identity & Multi-Tenancy | Verified Phase 0 and explicit authorization |
| 2 | Core Domain & API | Membership, tenant, and policy boundaries |
| 3 | Admin Web Dashboard | Authenticated core APIs |
| 4 | Technician Mobile App | Authenticated assignment/checklist/status APIs |
| 5 | Offline-First System | Correct online technician flow and aggregate versions |
| 6 | Advanced Features | Core workflows, clients, and reliable transaction boundaries |
| 7 | Quality & Hardening | Implemented product workflows to review and measure |
| 8 | Portfolio & Release Preparation | Reviewed release candidate and documented limits |

## Phase 0 — Foundation & Architecture

Deliver repository/tool inventory, requirements/scope analysis, this roadmap, architecture/security/data/API/sync designs, ADR, and AGENTS rules. Create the four-project .NET solution, unit/integration test projects, React/Vite admin shell, Expo Router mobile shell, strict TypeScript, locked dependencies, PostgreSQL EF connectivity, an empty schema baseline migration, live/ready endpoints, local Docker/MinIO infrastructure, basic GitHub Actions, and an honest README/local guide. No users, organizations, work orders, login UI, or fake statistics.

Acceptance: backend restore/build/format and foundation tests pass; web lint/typecheck/build/tests pass; mobile lint/typecheck/tests pass and Expo package compatibility is checked; Compose services start; live/ready respond correctly; the migration applies to real PostgreSQL and repeated application is safe; CI is defined. Native simulator/device validation and hosted CI execution must be reported separately from local checks. The implementation status includes exact outcomes and any unavailable tools.

## Phase 1 — Identity & Multi-Tenancy

Implement Identity user, organization, membership, invitation, and refresh session mappings/migrations. Add atomic initial owner registration, login/access JWT, hashed refresh rotation/reuse handling, logout/session revocation, current user, forgot/reset password, active organization selection/creation, invitation acceptance, role changes/deactivation, and named policies. Protect against last-owner removal. Add rate limits and web cookie CSRF/native credential transport decisions; configure delivery adapters without claiming external email is sent when credentials are absent.

Acceptance: register/login and first organization ownership work; another valid membership can be selected; invitation expiry/reuse/wrong recipient are rejected; refresh rotation and concurrent/reused-token behavior are tested; logout/revocation/deactivation block sessions; unauthenticated and wrong-role callers fail; Company A membership/organization IDs cannot expose or mutate Company B. Use real PostgreSQL integration tests for transaction and membership uniqueness behavior. Stop for the next phase gate.

## Phase 2 — Core Domain & API

Implement customers, locations, asset categories/assets, work orders, multiple assignments/primary technician, exact status transitions, comments/visibility, checklist templates and snapshots, required-item completion rules, status history, and audit logs. Add DTO validation, tenant-composite foreign keys, archive semantics, bounded pagination/filter/sort/search, explicit concurrency versions, and availability conflict/justified override rules. Serialize concurrent assignment/rescheduling checks to avoid check-then-insert races.

Acceptance: a manager creates customer→location→asset→work order, schedules it, and assigns technician(s); only valid state transitions succeed; required checklist items gate completion; stale versions yield safe conflicts; internal notes stay hidden; archiving preserves history; tenant/customer/location/asset constraints reject invalid relationships. Unit tests cover all transition pairs, business invariants, completion, overlap `[start,end)`, permissions, and versions. Real PostgreSQL tests cover workflows, constraints, concurrent assignments, audit atomicity, and IDOR/cross-tenant reads/writes.

## Phase 3 — Admin Web Dashboard

Build real authentication and protected routing, responsive layout/navigation, accessible forms, customers/locations/assets, work order list/create/detail/actions/history/checklist/comments, assignment UI, team/invitation management, and overview based on actual API aggregates. Add React Router, React Hook Form, Zod, Tailwind/component primitives, Lucide, and charts when their use cases land. Provide loading/error/empty states, confirmation/toasts, theme and localization structure; no inert navigation or placeholder actions.

Acceptance: manager completes a real API-backed workflow through the web UI; mobile-width and keyboard behavior work; input/API failures are actionable; unauthorized routes/actions are rejected by API and represented correctly. Component/form/error/protection tests pass. Playwright covers login→customer→location→work order→technician assignment and observes Assigned. Rich scheduling/analytics remain Phase 6.

## Phase 4 — Technician Mobile App

Implement Expo Router authentication, SecureStore session handling, assigned-task home/list/filter, work order detail, map linking, checklist updates, service notes/time actions, start/pause/resume/complete, profile/logout, and real API/error/loading/refresh flows. Add supported native libraries through Expo installation tooling. Location permission is requested only for a real location-dependent action; opening an address in maps does not require blanket device-location access.

Acceptance: technician logs in, sees only authorized assignments, opens detail, starts work, completes required checklist, adds notes, and completes an order; unassigned IDs fail. Logout clears session data. Component/transport/session tests pass and the critical flow is verified on a supported native development build/device or emulator; unavailable device/tooling is a stated limitation. Evaluate Expo-compatible native automation (for example Maestro) before adopting it; do not equate a web export with native testing.

## Phase 5 — Offline-First System

Implement SQLite migrations/cache/outbox, atomic local mutation+queue, secure local scope lifecycle, per-aggregate operation ordering, sync push receipts/idempotency, version conflicts, safe committed-order pull cursors, authorization changes/tombstones, retry/backoff, network recovery, offline indicator, and sync center. Finalize supported offline/replay/retention windows before enabling pruning. Add encrypted native persistence and verify cipher availability before claiming confidential offline data protection.

Acceptance: previously downloaded task opens offline; supported edits survive restart in the queue; reconnection pushes/pulls without duplicate mutation or silent overwrite; genuine conflicts are visible and recoverable; removed permissions purge unauthorized cache; logout/account switches remove local sensitive data. Tests cover duplicates/concurrent delivery, interruption/crash, delayed commits/cursors, dependent operations, protocol mismatch, expiry, rejection, and authorization changes, using real PostgreSQL and appropriate local/native persistence tests. See [offline-sync.md](offline-sync.md).

## Phase 6 — Advanced Features

Implement authenticated tenant/resource-scoped SignalR invalidations, post-commit transactional delivery, in-app and Expo push notifications/device lifecycle, safe private attachments/camera/gallery/retry, daily/weekly scheduling with technician filters/history and overlap warnings, and real reporting/analytics. Reassess a maintained storage provider before adding uploads; local MinIO is a development dependency with an archived-upstream limitation. Add drag/drop only after calendar semantics are correct.

Acceptance: assignment reaches only the correct authorized recipients; reconnect recovers through API reads; push delivery has configured credentials and observable failures; provider failure cannot roll back business changes; safe image upload/download rejects spoofing/oversize/cross-tenant access; adjacent and overlapping schedules behave correctly, including concurrency/DST; real dashboard figures match database fixtures. External native push credentials are an explicit operational dependency, never fabricated.

## Phase 7 — Quality & Hardening

Review tenant/IDOR/role/session/storage/sync security across all paths; expand adversarial integration and offline recovery tests. Measure representative queries and reduce N+1/indexing problems. Add OpenTelemetry tracing/metrics and reviewed correlation/log redaction, robust errors/rate limits/proxy config, accessibility improvements, and production/staging separation. Review dependency health, retention/backups and restore procedures, migration and concurrency behavior, and operational alerts.

Acceptance: documented critical security scenarios pass; representative end-to-end and recovery flows pass; production settings differ safely from development; measured queries and changes are recorded; accessibility issues and known limitations are documented. Do not declare production-ready solely from coverage percentage, successful build, or localhost screenshots.

## Phase 8 — Portfolio & Release Preparation

Polish README/architecture/ERD/API/sync/security/deployment guides to match reality. Add real screenshots and development-only synthetic seed: one organization, owner, manager, three technicians, ten customers, fifteen locations, twenty assets, varied orders, checklists, and history. Provide reproducible local demo/migration/test instructions, review CI from a clean checkout, run final acceptance, and document release/signing/staging/backup/security checklists. Do not auto-seed production.

Acceptance: an external developer can install locked dependencies, start containers/API/web, launch the documented mobile build, apply migrations, seed the demo, and follow the critical workflow. Tests and exact remaining limits are documented. Required cloud accounts, domains, TLS, email, push, storage, and signing credentials are provided through the deployment process, not committed to Git. No external deployment or paid resources are created without authorization.

## Phase-end evidence

At every gate list created/changed files, completed and pending tasks, architecture decisions, exact build/lint/typecheck/test commands and results, runtime/migration checks, blockers, and next-phase work. Update [implementation-status.md](implementation-status.md) before reporting completion. A local check and a remotely executed GitHub Actions run are distinct evidence.
