# PHASE 6 IMPLEMENTATION & RECOVERY REPORT

Branch `feat/realtime-notifications` at Phase 5 merge baseline `fcf7151`. Local verification on 2026-11-14 and 2026-11-15 (Europe/Istanbul). Physical-device validation for Phases 4–6 remains `DEFERRED / NOT VERIFIED` by owner decision.

This report records the implementation and interrupted-work recovery. The follow-up reports record the five Medium quality-gate fixes and the subsequent Edit focus and Save regressions. [M1–M5 remediation](phase-6-quality-gate-remediation.md) · [Final focus fix](phase-6-final-focus-fix.md) · [Final Save stability fix](phase-6-final-save-stability-fix.md)

## 1. Interrupted Work Assessment

The checkout contained unstaged Phase 6 changes and untracked collaboration modules, integration tests, the EF migration and the initial report. The recovery inspected the diff, lockfiles, migrations, provider contracts and regression evidence before editing. Backend build and both strict typechecks established a working baseline. The interrupted work was preserved on the same branch.

## 2. Previously Completed Features

The modular monolith adds a PostgreSQL-backed event and notification dispatcher, an S3-compatible attachment adapter, and workspace-zone scheduling. Application interfaces keep SignalR, Expo, and S3 provider details out of the domain. The implementation uses the existing organization write lock, audit and version rules. The roadmap adds day/week planning, technician filters, drag/drop after calendar semantics, and reporting to the prompt's detailed collaboration work. The calendar drop opens a proposed change in the existing confirmation workflow.

## 3. Recovered/Completed Features

The new PostgreSQL regression reproduced a dropped retry when the device rotated its Expo token. The dispatcher now retries unsent work against the current token of the same authorized installation, session and workspace. A provider receipt remains tied to the token that received the ticket. Recovery also added HTTP contract coverage for Expo ticket/receipt shapes and exercised orphan cleanup after storage writes an object but reports a failure.

| Area | Work items and assessment |
| --- | --- |
| SignalR | **COMPLETED & VERIFIED** — authenticated hub, tenant and role/assignment isolation, work-order publication after commit, browser and mobile API invalidation, reconnect reconciliation and offline compatibility. |
| Push | **COMPLETED & VERIFIED** — registration, revocation, rotation, dispatch, bounded retry and deduplication, Expo HTTP contract, workspace and session isolation. **IMPLEMENTED BUT UNVERIFIED** — native notification tap and lifecycle. **BLOCKED** — live Expo/APNs/FCM delivery and receipt acceptance require configured credentials and a physical device. |
| Attachments | **COMPLETED & VERIFIED** — private local MinIO, authorized metadata/upload/download, size and signature checks, web progress and preview, mobile upload API, retry and orphan cleanup. **IMPLEMENTED BUT UNVERIFIED** — mobile camera/gallery/document picker and native upload on hardware. |
| Scheduling | **COMPLETED & VERIFIED** — schedule and assignment rules, overlaps, UTC/DST, web calendar and live refresh, mobile schedule and cached Today, optimistic concurrency, audit history and reporting. |
| Security and regression | **COMPLETED & VERIFIED** — HTTPS, JWT/session, role and tenant/resource isolation, audit, concurrency, online API compatibility, PostgreSQL migrations and offline schema/queue compatibility in automated tests. **IMPLEMENTED BUT UNVERIFIED** — on-device SQLite/SQLCipher persistence through native restarts. |

## 4. SignalR Implementation

Work-order create/edit, transition, assignment, schedule, checklist, notes and attachment changes write an event in the same transaction as their business change and audit. The dispatcher reads committed rows using `FOR UPDATE SKIP LOCKED`. It routes only to server-tracked connections in the event's tenant and revalidates session and resource access. Former assignees receive an access-change hint with no resource ID. Non-manager invalidations omit detailed action names. Client deduplication is bounded by event ID and events trigger authoritative reads, including after reconnect. Out-of-order delivery never writes a payload into client state.

## 5. Push Notifications

Assignment changes, major state and schedule changes, and work starting within 30 minutes create private inbox entries. Owners and managers receive in-app alerts for critical work-order creation. Device registration is tied to the active technician session, membership, organization and installation. Tokens use server-side Data Protection with a hash for uniqueness. Opt-in is device-only and account/origin scoped on mobile. Retries, unique delivery keys, provider tickets, receipt polling, invalid-token retirement and current-session checks govern push. The Expo provider sends generic text and an opaque notification ID. [Expo send contract](https://docs.expo.dev/push-notifications/sending-notifications/)

## 6. Attachments & MinIO

Authorized users list and stream content through the API. Writes accept bounded JPEG/PNG/PDF data, validate magic bytes and file names, and generate GUID-based object keys. The API spools the bounded upload to a private temporary file and records a hidden reservation before the object write. Only committed ready metadata is visible. Retrying the same operation is idempotent. Abandoned reservations older than 24 hours are eligible for cleanup. The private local MinIO endpoint was verified using the maintained S3 SDK and private bucket. [Storage decision and deployment settings](adr/0002-local-object-storage.md)

## 7. Scheduling

The organizer's time zone drives daily and weekly intervals, displayed times, technician filters and the reschedule form. DST gaps or ambiguous new local times require a different input, while existing stored occurrences retain their original instant. Dragging to a date proposes local times for review through the same versioned API as keyboard rescheduling. Existing overlap conflicts and explicit audit-backed overrides remain authoritative. Reports add actual elapsed duration and distinct active orders with overlapping assignments.

## 8. Web/Mobile Integration

Authenticated layouts start one scoped WebSocket connection. Scoped query invalidation updates orders, schedules, reports, attachments and inbox data. Work-order detail includes progress, retry, safe image preview and authorized download. The inbox shows read status and only displays work-order links when the API returns authorized access.

The technician app displays Today and upcoming work, secure inbox and opt-in push controls. Notification taps route to the authenticated inbox for current access checks. Camera, gallery and system document pickers feed Expo's native streaming upload with progress and manual retry. Selected cache copies are removed on replacement, success or scope unmount. No physical hardware is required for the automated adapter and API tests.

## 9. Offline Sync Compatibility

Live updates nudge the existing Phase 5 coordinator. Work-order data, expected versions and pending operations still reconcile through the Phase 5 API/SQLite state machine. The Phase 6 SQLite v2 migration adds the authorized workspace time zone without replacing cached orders or pending operations. Offline Today reads the downloaded schedule. Attachments and inbox require connectivity.

## 10. Security

The hub uses existing bearer/session validation, configured web origins and authentication expiry. A browser upgrade query token is accepted only on the hub WebSocket route. Application client logging excludes SignalR token URLs. PostgreSQL tenant keys and resource checks protect inbox, push and attachment metadata. Attachments use an authenticated, `nosniff`, no-store response. Notifications carry minimal content. Docker verification uses isolated databases and preserves existing volumes.

## 11. Files Changed & Migrations

| Area | Scope |
| --- | --- |
| Backend | Application collaboration contracts, API hub/endpoints and JWT handling, Infrastructure dispatch, S3 adapter, attachment/device persistence, work-order audit integration, and reporting/sync projections |
| PostgreSQL | `20261114123641_CollaborationAndAttachments`, generated snapshot and protected downgrade |
| Web | SignalR hook, work-order attachments and notifications, workspace calendar, schedule forms and reports |
| Mobile | SignalR lifecycle, push inbox/device flow, file pickers/upload, upcoming work and SQLite schema v2 |
| Operations | Compose storage settings, locked AWS/SignalR/Expo/Temporal packages, and architecture, storage, database, API, mobile and status documents |

The recovery changed `CollaborationDispatcher.cs`, `FieldOpsDbContext.Collaboration.cs`, `ExpoPushProvider.cs`, `CollaborationTests.cs` and `ExpoPushProviderTests.cs`. It also updated the results, implementation status, architecture, database, security and deployment documents. The original Phase 6 migration and its generated snapshot apply to the recovered model.

## 12. Automated Tests

- PostgreSQL tests for hub negotiation, revoked sessions, current recipients, assignment removal, duplicate push and invalid-device receipts, attachment validation/failure/retry/isolation, and DST-adjacent versus overlapping schedules.
- Recovery adds a PostgreSQL rotation scenario, six Expo HTTP contract cases with a fake transport, and checks that orphan cleanup removes both abandoned metadata and stored bytes while preserving ready files.
- Browser test for automatic title refresh over a real WebSocket and private MinIO upload, preview and download. Mobile integration covers SignalR delivery and an authoritative read after reconnect, and separately checks private attachment upload, byte-for-byte download, duplicate suppression and cross-tenant denial.
- Web calendar tests cover both DST transitions. Mobile component tests cover notification opt-in and camera permission denial. The SQLite restart regression exercises the Phase 5 schema upgrade and queue preservation.

| Area | Observed result |
| --- | --- |
| Backend | Locked restore, Release build with zero warnings/errors, .NET formatting and EF model check passed. Full regression passed 247 tests (92 unit and 155 integration), zero skipped. Database cases used real PostgreSQL. All eight focused recovery cases passed, including six cases with a fake Expo HTTP transport. |
| Web | Linux lint, strict typecheck, format and production build passed. 60 Vitest and 13 Edge Playwright scenarios passed. The WebSocket refresh and private attachment scenario passed again against the recovered API image. The main Vite chunk was approximately 568 kB and triggers the 500 kB advisory. |
| Mobile | Clean Linux npm installation and peer check, lint, strict typecheck, format, Expo compatibility, 64 Vitest and 16 Jest tests passed. Android/iOS Hermes exports and Metro Android manifest/bundle smoke passed. |
| Mobile API | 10 integration tests passed against trusted local HTTPS, PostgreSQL, Mailpit and private MinIO. |
| Docker and migration | Compose configuration and rebuild passed on an isolated project. HTTPS live and ready passed. The local MinIO bucket returned 403 to anonymous listing. The migration job exited 0 and PostgreSQL listed all eight migrations. Disposable PostgreSQL tests cover clean migration, historical upgrade and preservation of work-order version and audit history. |
| Auth smoke | A separate normal-rate-limit HTTPS stack passed trusted TLS, CORS, CSRF, registration, SMTP confirmation, login, refresh, logout and rate limiting. The earlier E2E stack's higher test limits did not exercise the normal rate-limit threshold. |

Hosted GitHub Actions and native hardware were outside the run. The default development database and existing Docker volumes were preserved.

## 13. Remaining Limitations

Native Android/iOS, APNs/FCM delivery, native permission behavior and actual Expo delivery receipts are `DEFERRED / NOT VERIFIED`. Local provider tests use fake Expo responses. A provider handoff followed by an interrupted acknowledgement can still cause a repeated generic push. The in-app entry remains unique. The real-time connection registry assumes a single serving replica. Local MinIO is archived and development-only. Production storage credentials, malware scanning and a separate provider acceptance run remain open. Image signature validation checks the declared format header. Attachments are online-only. Clean Linux npm installation reported existing transitive dependency advisories (30 moderate and one low at run time).

## 14. Phase 7 Readiness

- **PHASE 6 IMPLEMENTATION: COMPLETE**
- **INDEPENDENT QUALITY GATE READINESS: READY**
- **PHASE 7 READINESS: CONDITIONAL**

Final mobile acceptance requires the deferred physical-device checks. Production push and storage retain the acceptance dependencies above. Phase 7 requires separate authorization.
