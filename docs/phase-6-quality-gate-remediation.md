# PHASE 6 QUALITY GATE REMEDIATION REPORT

Local verification on 2026-11-15 (Europe/Istanbul), branch `feat/realtime-notifications`. The independent gate supplied five Medium findings. The working tree already contained the Phase 6 implementation. The default development database and existing Docker volumes were preserved.

The independent re-audit subsequently confirmed M1–M5 and found a separate Edit focus regression. [Final focus fix](phase-6-final-focus-fix.md)

## 1. Root Causes

| Finding | Cause |
| --- | --- |
| M1 | SignalR filtered the parent work-order query and missed the separate comments, checklist, status-history and audit keys. |
| M2 | The dispatcher treated an old-token rejection as terminal after rotation. A rejected, rate-limited receipt kept its ticket, and each new ticket reset the retry counter. |
| M3 | The mobile listener returned immediately when a registration promise was pending, dropping the newer token. |
| M4 | The attachment list rendered stored metadata. The only preview URI came from the local upload picker. |
| M5 | Upcoming work advanced the current instant by 144 hours before extracting the workspace date. A DST transition could move that result to an adjacent date. |

## 2. M1–M5 Fixes

- **M1** invalidates the active tenant, user, role and session's collaboration queries on live events and reconnect. Debounce and bounded event-ID deduplication remain in place. API reads provide the current version and content.
- **M2** keeps one delivery per notification and installation/session. A definitive rate-limit receipt or an old token's `DeviceNotRegistered` receipt clears the rejected ticket when another attempt is allowed. The next send uses the current eligible device token and the same notification ID. Pending and temporary receipt failures retain their ticket. Backoff, eight failures and the delivery lifetime bound retries across sends and receipts.
- **M3** queues the latest rotation while registration is pending. The current registration and later rotations are serialized. Old scope callbacks cannot register against a new scope. Effect cleanup cancels requests and removes listeners.
- **M4** adds an authenticated **View** action to stored files. It streams bytes to app-private cache, checks the response MIME and byte count against the listing, previews images in-app and opens PDFs through Expo Sharing. The download has an 8 MiB limit and rejects redirects. Only a local file URI is given to Expo Sharing. [Expo FileSystem](https://docs.expo.dev/versions/v58.0.0/sdk/filesystem/) · [Expo Sharing](https://docs.expo.dev/versions/v58.0.0/sdk/sharing/)
- **M5** advances the workspace's local calendar date by six days and uses the next local midnight as the exclusive UTC end. The lower bound remains the current instant.

## 3. Files Changed

| Area | Files |
| --- | --- |
| Backend | `CollaborationDispatcher.cs`, `CollaborationTests.cs` |
| Admin web | `src/api/realtime.ts`, `e2e/admin.spec.ts` |
| Mobile | `src/api/transport.ts`, `src/auth/session.ts`, `src/features/notifications.tsx`, `push-native.ts`, `attachments.tsx`, `OrdersScreen.tsx`, `orders.ts`, `orders.test.ts`, `mobile.spec.tsx`, `integration/technician.test.ts` |
| Dependency | `apps/mobile/package.json` and root `package-lock.json`, adding Expo SDK 58-compatible `expo-sharing` |
| Documents | This report, `phase-6-results.md`, `implementation-status.md`, `mobile.md` and `security.md` |

The existing EF migration and schema support the corrected delivery lifecycle. The API routes and JSON contracts are unchanged.

## 4. Added Regression Tests

Added one browser scenario, two PostgreSQL cases, nine React Native component cases and six scheduling cases. Extended the prior PostgreSQL rotation test to assert delivery of the original notification ID and the mobile API test to exercise the authenticated binary response path. Focused pre-fix runs reproduced the five reported behaviors. The existing contrast scenario now waits for a visible report tile before taking measurements. The initial full browser run reached the dashboard shell before its report data was rendered in that scenario.

## 5. SignalR Verification

In Edge Playwright, a manager held an order open while a separate authenticated owner session committed a comment and changed the checklist. The browser showed the comment, checklist and audit updates over a real WebSocket without manual refresh. The test interrupted the connection during a scheduling change. A second connected page observed the committed change before the first page reconnected and read the new status history from the API.

## 6. Push Delivery Verification

Real PostgreSQL tests confirm a replacement token receives the same notification after the prior token's rejected receipt. They also cover rate-limited receipts, backoff, a single delivery row, the cross-ticket retry limit and cancellation of a queued resend after logout. Provider responses are simulated using the Expo contract. [Expo push tickets and receipts](https://docs.expo.dev/push-notifications/sending-notifications/)

React Native tests exercise a held registration, coalescing multiple token events, repeated events and cancellation on logout, workspace switch and unmount.

## 7. Mobile Attachment Verification

The mobile API integration uses the production session transport to check authorized binary download byte-for-byte from MinIO and cross-tenant denial. The React Native suite checks image preview, PDF handoff, access failure, partial download cleanup and logout while streaming. Completed preview files are purged on close, replacement or unmount. The next preview initializes the private cache and clears files left by a previous app process. Offline attachment viewing remains unavailable.

## 8. DST/Scheduling Verification

With the host set to `Pacific/Honolulu`, the New York fixtures resolve March 10 at 23:30 through March 16 local time and November 5 at 00:30 through November 11 local time. Normal weeks, month and year boundaries, Nepal's fractional UTC offset, Auckland and UTC also passed. The API continues receiving UTC range endpoints.

## 9. Security Regression

The Release regression exercises JWT/session revocation, tenant and role boundaries, SignalR recipients, attachment authorization and private object access, optimistic concurrency, audit history and offline replay. The attachment transport checks the originating session and never places credentials in the share URI. Android/iOS runtime validation of SQLCipher, SecureStore and the share sheet remains deferred.

## 10. Full Test Results

| Area | Observed result |
| --- | --- |
| Backend | Locked restore, Release build (zero warnings/errors), .NET format and EF model check passed. 92 unit plus 157 integration tests passed, with zero skips. Database cases used real PostgreSQL. All 16 focused collaboration and provider cases passed. |
| Linux clients | Clean `npm ci` and peer tree, lint, strict typecheck and formatting passed. Web passed 60 Vitest and the production build. Mobile passed 70 Vitest and 25 Jest cases. Expo compatibility, Android/iOS JavaScript exports and Metro smoke passed. |
| Browser | 14 of 14 Edge Playwright scenarios passed against the recovered Compose API after the contrast test was made to wait for rendered report data. |
| Mobile API | 10 tests passed against trusted HTTPS, PostgreSQL, Mailpit and private MinIO. |
| Infrastructure | Isolated Docker configuration/rebuild, HTTPS live/ready and migration job passed. Anonymous MinIO bucket listing returned 403. |

## 11. Remaining Issues

Physical Android/iOS validation for Phases 4–6 remains `DEFERRED / NOT VERIFIED`. Live Expo/APNs/FCM delivery and native file viewing/handoff require device acceptance. A receiving app can keep a copy of a PDF shared by the user. Production object storage and hosted GitHub Actions were outside this local run. The existing Vite bundle advisory and 31 low/moderate npm advisories remain.

## 12. Phase 7 Readiness

- **PHASE 6 IMPLEMENTATION: COMPLETE**
- **INDEPENDENT RE-AUDIT READINESS: READY**
- **PHASE 7 READINESS: CONDITIONAL**

Phase 7 remains subject to the targeted independent re-audit and explicit phase authorization. The physical-device decision remains in force for final mobile acceptance.
