# Phase 4 — Technician mobile results

Date: 2026-11-08 (Europe/Istanbul). Branch: `feat/technician-mobile`. One agent; no delegation, commit, push, deployment or Phase 5 implementation.

## Scope and delivered behavior

The original master plan's Phase 4 tasks and the repository roadmap map to a working online native client:

| Requirement | Implementation / evidence |
| --- | --- |
| Expo Router/navigation | Protected sign-in, authenticated Today/Work orders/Profile tabs and Technician-only detail route; unsupported roles select a Technician workspace |
| Authentication / secure session | Existing native JSON endpoints, SecureStore refresh proof/expiry, memory-only access, serialized one-use rotation, crash markers, revocation cleanup and generation checks; session tests and real login/selection/refresh/restart/logout integration |
| Technician home / assignments | Organization-zone today's schedule and all own assigned work, real search/status/priority filters, priority/status badges, pagination, pull-to-refresh, loading/empty/error states |
| Work-order detail | Customer, address/contact, asset/model/serial, primary/additional staff, planned/actual times, paged status history and safe native map link |
| Checklist | Immutable server snapshot, required/optional items, completion progress and root aggregate version acknowledgement; completion disabled until requirements pass |
| Notes | Trimmed/length-limited technician-visible service notes; drafts retained on failure and cleared after acknowledgement; internal notes excluded by server |
| Status and time actions | Start/pause/resume/complete, captured confirmation version, shared write lock/pending state, server-controlled actual timestamps and explicit stale-version recovery |
| Profile/logout | Authoritative profile, workspace switch, recovery request and immediate local sign-out/cache cleanup; unconfirmed remote revocation reported honestly |
| Tenant/assignment security | Server membership/resource authorization unchanged; real unassigned reads/writes/history/checklist fail with 404; another tenant cannot read an order; old-scope responses discarded |

Actual UI uses React Native primitives, safe areas, keyboard avoidance, 50-point buttons, accessible labels/checkboxes/radios/tabs, scalable text, field contrast and native confirmation/selection modals. English presentation copy is in the mobile message dictionary. Only Ionicons is imported so exports include its font rather than every icon family. No unimplemented notifications, attachments or sync navigation was added. Those master-plan capabilities belong to Phases 5/6 per the roadmap.

There is no separate time-entry endpoint in the existing API. The app uses start/pause/resume/complete timestamps/history and does not invent billing duration or a client-controlled actual time. No authentication, authorization, tenant, DTO, backend write transaction or migration contract changed. See [mobile architecture, security and setup](mobile.md) and the [file inventory](phase-4-files.md).

## Architecture decisions

`MobileSession` is framework-independent and accepts a transport, protected-store adapter and cache-clear callback. The native runtime supplies SecureStore; tests can exercise the actual coordinator without claiming a mock is device storage. Refresh proof persistence precedes publishing memory access. A rotation marker precedes consuming one-use proof; interruption requires reauthentication. Logout tries both a signed-out tombstone and deletion **before any server network wait** and locks the session if neither succeeds. Queued 401 cleanup rechecks the original generation before touching the current session.

TanStack Query owns nonpersistent remote state, scoped by tenant/user/role/session. Scope changes cancel/clear queries and increment a generation guard. Foreground profile revalidation reconciles revoked/demoted membership; the backend remains authoritative. All work-order mutations share a scoped QueryClient lock, acknowledge parent/checklist versions, and remain pending through refetch. Confirmations keep their original version. No automatic command replay or silent conflict overwrite was introduced.

Expo-supported SecureStore/font/system UI packages and pinned compatible Jest Expo/React Native Testing Library dependencies were installed in the existing npm workspace. Strict TypeScript and existing lint rules remain enabled. A dedicated Prettier check is now part of mobile CI. The existing disposable infrastructure CI job runs real mobile HTTP/session integration after its browser workflows; this is Node-driven transport integration, not an Android UI test.

The complete dependency-tree check also exposed an existing optional-peer mismatch: the web form resolver found ESLint's Ajv 6 although its optional adapter requires Ajv 8. Pinning compatible Ajv 8.20.0 as a web development dependency preserves ESLint's separate Ajv 6, makes `npm ls --all` pass and leaves web application source/behavior unchanged. Both client CI jobs now verify the complete peer tree after installation. Web checks were repeated after this fix.

## Executed verification

| Check actually run | Result |
| --- | --- |
| Root `npm ci` and complete peer tree | PASS; clean locked install; compatible separate Ajv 6/8 resolutions; `npm ls --all` exits 0 |
| Mobile lint / strict typecheck / Prettier | PASS; zero warnings permitted; integration sources included in TypeScript checking |
| `npm run mobile:test` | PASS: **55 Vitest + 13 React Native component tests**, zero skipped |
| `npm run test:integration --workspace=@fieldops/mobile` | PASS: **7 tests** against real HTTPS API, PostgreSQL and Mailpit; final run about 11.1 seconds |
| `npm run mobile:check` | PASS; Expo-supported dependency versions |
| `npx expo-doctor` | PASS: **20/20 checks** after dependency changes |
| `expo prebuild --platform android --no-install` | PASS; generated ignored native project, backup references and blocked storage/vibration permissions inspected; no location permission |
| `npm run mobile:export` | PASS: Android and iOS Hermes bundles, approximately **2.8 MB each**, 34 exported assets; this is JavaScript packaging |
| `npm run mobile:smoke` | PASS: actual Metro startup, Android manifest and **6,195,972-byte** development HTTP JavaScript bundle |
| Backend Release build | PASS: **0 warnings, 0 errors**, unchanged backend source |
| Backend full regression | PASS: **92 unit + 135 real PostgreSQL integration = 227 tests**, zero skipped; integration assembly about 64 seconds |
| Web lint / strict typecheck / test / production build | PASS: all **58 existing tests** preserved; existing 502.94 kB main-bundle advisory retained |
| Docker/API smoke | PASS: fresh isolated Compose bootstrap/migration/readiness, actual live/ready Healthy responses; default development readiness still Healthy |
| Workflow / diff | PASS: local actionlint 1.7.12 and `git diff --check` |
| Android native run | **BLOCKED**: prebuild succeeded; SDK path absent and `adb` unavailable. No APK compile/install or device flow ran |
| Hosted CI / additional browser E2E | **NOT RUN**: no remote workflow triggered; Phase 3 Playwright was not repeated for unchanged web source |

The initial full backend attempt used `Host=localhost` against an IPv4-only published Docker port. TCP inspection showed `::1:5550` SYN-SENT waits. That attempt was interrupted, not marked passed; rerunning the unchanged suite with `Host=127.0.0.1` completed successfully. No checks were disabled or backend code changed to hide the environment problem.

## Regression coverage

The original 23 mobile health tests remain. New pure tests cover protected credential content, single-flight rotation and waiting requests, interrupted markers, expired/wrong-origin proof, storage/write/delete failures, offline/logout interruption, malformed session/results, late scope responses, role changes, stale 401 during workspace rotation, 401 without mutation replay, TLS/debug-origin policy, bearer/no-cookie native transport, cancellation/deadlines including response-body reads, safe malformed ProblemDetails, DST day boundaries and encoded map URLs. Final review reproduced two session edge cases with failing tests first: delayed logout previously left ready proof until its network request finished, and an old 401 queued behind organization selection could erase the new session. Both regressions passed after moving local cleanup before network I/O and checking generation again inside the credential queue.

Thirteen native-component tests cover sign-in success/error and password masking; own-assignment search; accessible status filter; start/version/checklist coordination; disabled aggregate controls while pending; required-checklist completion guard; failed-note draft retention and explicit reload; successful-note clearing; removed-assignment redaction; static map opening; profile/logout cache clearing; and same-tick write serialization. Jest Expo mocks native boundaries, not the session/query/write implementation.

Seven integration scenarios use unique real SMTP-activated owner/technician/other-tenant accounts and invitation/organization selection. They exercise actual assigned lists, detail, required checklist rejection, start/check/note/pause/resume/complete and server timestamps, real stale-version rejection/no duplicate note, unassigned IDs across reads and writes, internal-note and cross-tenant denial, rotating restart/logout proof invalidation, and server session revocation. Backend 227-test regression retains earlier migration, rollback, concurrency, audit and tenant-isolation guarantees.

## Database safety

No new migration is required. Fresh `fieldops-phase4` uses PostgreSQL **5550**, API HTTP **5090** / HTTPS **5450**, Mailpit SMTP **1040** / HTTP **8040**, with the existing local certificate. The Compose migration job applied the existing chain there. Backend regression creates/drops its own generated databases on this isolated server. The default development database was not migrated, seeded, recreated or deleted. No historical audit values or operational data were fabricated. The isolated test containers were stopped after verification; their fixture volumes remain local. The default development stack remains healthy. No credentials or mail/token artifacts are committed.

## Remaining issues and readiness

The [Android validation preparation follow-up](phase-4-android-validation.md) records the EAS profiles, HTTPS setup, local verification and Galaxy A34 handoff from 2026-11-12.

**Native acceptance is not verified.** This Windows host has no configured Android SDK/ADB/JDK or native device/emulator; iOS requires macOS/Xcode. The Android command's concrete failure is recorded above. On-device SecureStore persistence/backup behavior, process restart, native keyboard/screen-reader behavior and the complete technician UI flow must be tested on a supported target. [The exact acceptance sequence](mobile.md#native-acceptance-gate) includes normal, network, conflict, authorization and logout cases. Maestro was evaluated using its official Expo/native-device workflow and prerequisites; no unexecuted automation is presented as passing.

The root npm audit reports **33 advisories: 32 moderate and 1 low, zero high/critical**, involving `minimatch`, `glob`, `deepmerge` and their dependent build/test packages. Suggested automatic fixes include incompatible toolchain/SDK downgrades; no forced audit fix was applied. These remain dependency maintenance work. The prior web bundle-size warning is also retained. Native gate and dependency limitations are explicit, not hidden by skipped tests or raised check thresholds.

**Phase 4 online implementation: delivered and automatically verified within the stated limits. Phase 5 readiness: NOT READY until native acceptance is completed and reviewed.** Phase 5 was not started and requires separate explicit authorization.
