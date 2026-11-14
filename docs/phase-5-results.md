# Phase 5 implementation report

Verified locally on 2026-11-14 (Europe/Istanbul) on `feat/offline-sync`. Phase 4 Android native validation is `DEFERRED / NOT VERIFIED` by owner decision.

## 1. Architecture

The Expo client adds a scoped local workspace and sync coordinator around the existing online technician screens. The modular monolith adds sync endpoints and an application contract. Infrastructure reuses the work-order, checklist and comment rules inside the sync transaction. Existing operational routes and clients continue to use their original API contracts.

## 2. Offline storage

Expo SQLite uses a versioned schema for downloaded assigned orders, checklist data, up to 10 technician-visible notes per order, outbox and incremental cursor. The UI derives local changes from committed queue rows. A failed page rolls back both cache changes and the next cursor. A supported native build must report SQLCipher before the app uses its cache.

## 3. Operation queue

The allowlist covers checklist toggles, technician-visible service notes, start, pause, resume and completion. A command keeps its operation ID, version, payload, local capture time and sequence across restart. The client recovers pending delivery after an interruption. The server retains the client capture time with the applied receipt and records actual execution times using its own clock. Assignment and administration require connectivity.

## 4. Sync engine

Expo Network events, foreground lifecycle, periodic checks and manual sync trigger bounded drains. Failed commands use ordered retries with durable rate-limit waits. PostgreSQL atomically commits receipts, versioned writes, history/audit and feed updates. Repeatable-read pull uses a transactional tenant watermark. Lost replies replay an identical command and reconcile the receipt once the resulting version is present locally.

## 5. Conflict handling

Stale versions return a batch conflict result with the current authorized version. Conflicted or rejected edits hold the dependent tail for the order. Sync shows the local intent and current server status or checklist state. Explicit reapply uses a new UUID and current version, with dependent edits held for review. Explicit discard removes the selected edit and its dependent tail.

## 6. Security

The native SQLCipher key, refresh proof and workspace identity use device-only SecureStore. The bearer lives in memory. The encrypted cache is scoped by origin, tenant, user, session and role. Writes revalidate membership and assignment before exposing a receipt. Logout and session/workspace change remove local proof and key. An assignment change invalidates the cursor and removes inaccessible cached data during the authorized resnapshot. Local access lasts up to 24 hours. New commands and cursor reuse have a seven-day window. No receipt pruning is enabled. [Detailed security design](security.md#phase-5-offline-storage)

## 7. Files changed

| Area | Files |
| --- | --- |
| Expo configuration | `apps/mobile/app.json`, `apps/mobile/package.json`, `package-lock.json` |
| Mobile offline | `apps/mobile/src/offline/{store,engine,vault}.ts`, `runtime.tsx`, `screens.tsx`, new Sync route and tab |
| Mobile integration | Session runtime and controller, root and technician routes, Profile, order list/detail and workspace query hooks |
| Backend | `SyncEndpoints.cs`, `ISyncService.cs`, `SyncService.cs`, `FieldOpsDbContext.Sync.cs`, API/DI registration, existing work-order and detail services |
| Database | `20261113203829_OfflineSynchronization.cs`, migration designer and model snapshot |
| Tests | `OfflineSyncTests.cs`, downgrade regression, `store.test.ts`, `session.test.ts`, `mobile.spec.tsx`, `integration/technician.test.ts` |
| Documentation | README, API/database/security/mobile/offline references, Phase 4 reports, implementation status and this report |

## 8. Added tests

- PostgreSQL covers simultaneous duplicate delivery, payload mismatch, audit uniqueness, cross-tenant cursor/command rejection, assignment removal and session revocation, stale/expired commands and independent work-order progress. It also verifies delayed commits, forced receipt failure rollback, and backfill from the previous migration without history changes.
- Real SQLite covers transactional page rollback, in-flight restart, ordered overlays and replay, rate-limit persistence, conflict recovery and access-loss resnapshot. Session tests cover offline restart, logout, server revocation and workspace cleanup.
- React Native component coverage switches from an online detail to a downloaded order, preserves an unsent note draft, and sends the save to the local outbox. The real HTTPS integration runs the SQLite outbox against ASP.NET Core and PostgreSQL, including checklist, loss after commit and single-note replay.

## 9. Regression results

| Check | Observed result |
| --- | --- |
| Backend | Locked restore, Release build with zero warnings/errors, and `dotnet format` passed. 92 unit and 140 real PostgreSQL integration tests passed, zero skipped. EF model matches the migration. |
| Mobile | Lint, strict typecheck, Prettier, Expo SDK compatibility, 64 Vitest and 14 React Native component tests passed. |
| Mobile HTTP integration | 8 passed against the isolated HTTPS API, PostgreSQL and Mailpit. |
| Expo | Android/iOS Hermes JavaScript export, Metro Android manifest and HTTP-bundle smoke passed. Config introspection shows SQLCipher enabled for both platforms. |
| Web | Lint, strict typecheck, Prettier, production build, 58 unit/component and all 12 Edge Playwright tests passed. The existing 503 kB bundle advisory remains. |
| Docker and API | Isolated migration job exited 0. PostgreSQL and Mailpit passed container health checks. HTTPS live/ready returned Healthy, anonymous sync returned 401, and all seven migrations were present. |

The default development database and existing Docker volumes were preserved. The isolated PostgreSQL test suite uses disposable test databases. Hosted CI was outside the local run.

## 10. Known limitations

Physical Android/iOS validation, native SQLCipher runtime/file behavior and backup/restore remain `DEFERRED / NOT VERIFIED`. JavaScript export and Node SQLite do not exercise the native encryption module. The 24-hour window bounds normal offline revocation latency on a disconnected device. Sync runs in the foreground and on resume. The cache excludes detailed contacts/maps, team, history and older note pages. Receipt retention is currently indefinite. [Device acceptance sequence](mobile.md#phase-5-native-acceptance)

## 11. Phase 6 readiness

**Independent Quality Gate: READY.** Local automated evidence and implementation are available for independent review. **Phase 6 readiness: CONDITIONAL.** Final mobile acceptance depends on physical-device validation of Phases 4 and 5. Phase 6 requires separate authorization.
