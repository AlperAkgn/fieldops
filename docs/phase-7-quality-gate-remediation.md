# Phase 7 quality gate remediation

## 1. Root cause analysis

The supplied independent audit reported security PASS, observability and reliability FAIL, performance CONDITIONAL, and Phase 8 NOT READY. Its findings were reviewed against `feat/security-observability`, based on Phase 6 merge `df61ccc` with the uncommitted Phase 7 implementation already present. The supplied audit brief was the finding source. The working tree contained no separate Phase 7 independent report or trace for the intermittent Edge failure.

## 2. Findings P7-01–P7-07

| Finding | Confirmed cause or test evidence |
| --- | --- |
| P7-01 | Request telemetry recorded duration in the middleware `finally` after SignalR relinquished the underlying WebSocket request. The upgrade boundary was absent from metrics. |
| P7-02 | The dispatcher reused the batch-start UTC value to persist all completions and compute retry deadlines, including rows committed after that instant. |
| P7-03 | The S3 `GetObjectAsync` cancellation scope ended once headers arrived. The API then copied the open provider stream with the client-abort token alone. |
| P7-04 | Connection and command timeouts could be sequential. The ASP.NET health-check timeout signalled cancellation and still waited for the dependency to return. |
| P7-05 | The original `Down` dropped every retry field. A subsequent `Up` recreated the columns with defaults, leaving terminal events eligible again. |
| P7-06 | One batch of at most 25 each three seconds created an 8.33 events/s ceiling and a polling floor close to three seconds. The new 160-event workload reproduced throughput saturation. |
| P7-07 | The original Edge trace was unavailable and the intermittent failure was not attributed to production code. The test had two synchronization gaps. It could intercept detail reads before the page had rendered the fixture, and it matched raw SignalR frames using substrings and an exact stand-alone handshake message. |

## 3. Implemented fixes

- Record successful WebSocket upgrade and subsequent connection lifetime as bounded operations. Preserve HTTP duration for ordinary requests and rejected upgrades. Keep correlation and tracing on the original activity.
- Read UTC per claim and at the end of each delivery attempt. Clamp clock corrections to the later of creation and claim. Start backoff at the transition and keep stable event IDs.
- Carry a linked eight-second deadline through the attachment fetch and body transfer. Preserve streaming, the known content length and fresh tenant authorization. After headers, abort the transport on failure and dispose the provider response.
- Use one independent PostgreSQL probe connection with short driver timeouts and cancellation. Give the DB probe 3.5 seconds, the health registration four seconds, and the HTTP response a separate 4.5-second budget. Keep liveness independent.
- Reject lossy Phase 7 downgrades before changing the index, columns or migration history.
- Poll realtime work after 500 ms when caught up and yield 50 ms after a full batch. Keep sequential per-event transactions, authorization checks, `SKIP LOCKED` and a separate three-second housekeeping cadence in the same worker. Back off for three seconds after an unexpected worker failure.
- Require a ready API, successful workspace selection, the exact initial detail response and rendered order heading before installing the Edge race gates. Reuse parsed SignalR messages keyed by order and version. Preserve the real HTTP 409 and focus assertions. Keep work-order controls available through background reads while preserving the aggregate write lock through the authoritative post-write reload.

## 4. Files changed

See the [Phase 7 file inventory](phase-7-files.md#quality-gate-remediation). Changes are limited to backend telemetry, streaming, readiness, dispatch/migration compatibility, the affected Edge fixture, and local verification tooling and documentation.

## 5. New regression tests

- `SocketUpgradeAndLifetimeStayOutsideTheNormalHttpHistogram` runs authenticated SignalR through TestServer with real PostgreSQL, verifies ordinary GET and POST, an HTTP error, failed upgrade, and a connection deliberately kept open over 400 ms. The test inspects duration samples by trace ID and flushes the SDK to a controlled loopback OTLP/HTTP receiver.
- Outbox tests cover a second tenant's event committed during a batch, slow delivery and failure timestamps, exact retry eligibility on a resumed worker scope, stable identity, terminal failure after a backward clock adjustment and refusal of downgrade with terminal state.
- Health tests exercise a silent TCP peer, a dependency that delays cancellation cleanup, cancellation propagation and independent liveness. Docker verifies repeated readiness failures with a real paused PostgreSQL backend.
- The private MinIO proxy and validated download client exercise a real small and 6 MiB object, delayed headers, eight bytes then a stalled body, upstream cleanup, client disconnect, a cross-tenant 404, and an exact-byte retry.
- The two existing Edge focus tests now wait for a real initial detail response and use exact-version SignalR gates. Held real detail GETs verify the scheduling dialog opens and an independent order event cannot drop a checklist click. A buffered owner event preserves the stale browser snapshot through a real HTTP 409 in the transition test. Conflict, version and focus assertions remain in place.

## 6. Database migration safety

The `Up`, migration ID and EF model are preserved. The `Down` acquires an exclusive lock on `WorkOrderEvents` in the migration transaction, then rejects the downgrade when any attempt count, scheduled retry or terminal timestamp would be lost. The lock prevents an intervening delivery write between the check and DDL. A downgrade is allowed only when retry state is empty. The old schema also lacks optional Phase 7 tracing metadata. Use a tested, coordinated restore or a forward fix for rollback involving persisted delivery state. [Migration rollback procedure](operations.md#backups-and-restore)

Real PostgreSQL integration tests cover empty installation, Phase 6 upgrade/reapplication, historical rows, guard failure with terminal state, unchanged schema history and no redispatch after refusal. In the separate `pg_dump -Fc` and `pg_restore` rehearsal, the CLI downgrade exited 1 with the expected guard and the CLI reapplication exited 0. The restore copy retained nine migration rows, 306 orders, 1,225 audit records, its terminal record with eight attempts, and the outbox foreign key.

## 7. OpenTelemetry measurements

`fieldops.duration` with `operation=http` retains normal HTTP request timings, including rejected upgrades. `websocket.upgrade` measures request start to successful HTTP/1.1 status 101 or HTTP/2 CONNECT response headers. `websocket.connection` begins at that boundary and ends on close, with success, failure or cancellation classification. Neither socket duration enters the `http` series. The local OTLP test observed both socket series, and the successful WebSocket retained the supplied trace ID in its samples. The dedicated test exercises HTTP/1.1.

## 8. MinIO timeout behavior

The original container completed the artificially stalled body past the deadline, and the same drill exited 1. The remediated Kestrel container with real MinIO through a private network proxy exited 0. Normal 156-byte and 6,291,456-byte objects, cross-tenant 404 and exact-byte retry passed. A pre-header stall returned 503 in 8,017 ms. An eight-byte partial response retained status 200 and interrupted the transport in 8,013 ms. The proxy saw incomplete upstream closure at 7,999 and 7,997 ms for the timed-out cases and at 5 ms after the separate client-disconnect case.

## 9. PostgreSQL readiness behavior

Pausing PostgreSQL in the isolated Compose project yielded three consecutive HTTPS readiness responses with HTTP 503 in 4,037, 4,037 and 4,026 ms from the PowerShell client. Liveness stayed at 200 both before and after the failing probes. Readiness recovered to 200 in 2,045 ms after resume. Client timings include connection setup. The refused-connection and delayed-cleanup tests cover complementary paths. The dedicated connection stays out of the application request pool and gets disposed within the probe scope.

## 10. SignalR performance before and after

The original audit's 156-event peak and four-event tail measurements describe a different fixture. A matched before/after run on the same isolated Linux Compose stack used two authorized WebSockets, a seeded tenant, and a drained queue before each workload. The baseline API image was preserved before the remediation build. Windows host used a Core i7-12700H and 20 logical CPUs. Docker reported 20 logical CPUs and 15.46 GiB RAM. External push delivery was disabled. Latency measures from write submission to receipt on both sockets. Peak pending is sampled and can miss short spikes.

| Workload | Samples | Produced/s | Dispatched/s | Peak pending | Drain after producer (ms) | p50 / p95 / p99 (ms) |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Low before | 20 | 0.35 | 0.33 | 1 | 2,802.83 | 2,775.10 / 2,811.87 / 2,812.19 |
| Low after | 20 | 2.07 | 2.01 | 1 | 272.90 | 304.22 / 323.21 / 323.99 |
| Burst before | 160 | 10.58 | 8.40 | 49 | 3,926.94 | 3,203.99 / 5,334.21 / 5,882.91 |
| Burst after | 160 | 10.58 | 10.56 | 5 | 29.74 | 250.63 / 487.36 / 509.25 |

All four runs finished without terminal events, retries or duplicate socket observations. The low workload waits for delivery before submitting the next item, which accounts for its increased production rate. The burst targets one mutation per 95 ms independently of socket delivery. A 20-sample low-load p99 is the maximum. The 160-sample burst tail has more observations than the preliminary four-event benchmark.

| Workload | Mean API CPU | Mean PostgreSQL CPU | Last API / PostgreSQL memory | PG commits / blocks read / hits |
| --- | ---: | ---: | --- | --- |
| Low before | 3.58% | 10.59% | 185.2 / 44.87 MiB | 843 / 59 / 77,381 |
| Low after | 17.47% | 12.09% | 178.5 / 84.09 MiB | 504 / 0 / 25,101 |
| Burst before | 20.39% | 16.24% | 192.8 / 44.45 MiB | 1,185 / 2 / 42,502 |
| Burst after | 17.58% | 16.99% | 184 / 84.52 MiB | 1,147 / 0 / 55,842 |

Metrics are container samples and database-wide counters. Low runs had different durations, PostgreSQL stayed up across trials, and cached data/fixtures grew. The after worker polls an idle outbox up to twice per second. This trades additional idle PostgreSQL checks for lower foreground delay. Staging should measure idle utilization, tenant mix and enabled provider costs. For a single healthy replica with approximately 10.5 writes/s and two local receivers, candidate investigation thresholds are p95 under one second at low load and under two seconds in a burst. The roadmap defines no SLO and these thresholds require approval before becoming targets.

## 11. Web E2E stability

The remediation preserves the real 409, the user's draft, keyboard focus restoration and pending GET coordination. Readiness, detail load and protocol handshake are independently asserted. No waits were lengthened and Playwright retries remain zero. The intermittent original failure still lacks a reproducible application cause.

The full remediation runs passed the audited focus case and exposed a shared control race at the newer dispatch cadence. The checklist trace showed an overlapping detail GET and no second PATCH. The manager workflow subsequently clicked Schedule without opening its dialog. Both failures were reproduced deterministically with a held detail GET. The order detail actions, scheduling and assignment forms, and collaboration controls now use the existing aggregate write state. Role eligibility, terminal-state checks and the captured-version guard stay in force. The browser regressions preserve checklist write versions 5, 6 and 7, a real HTTP 409 on the stale transition, and explicit reload.

## 12. Remaining risks

- The current transport uses one API replica. Multi-replica SignalR, hosted GitHub Actions, staging, production object storage and full image CVE scanning remain unverified.
- Native Android/iOS validation remains `DEFERRED / NOT VERIFIED`. The mobile automation uses builds, component tests and a real local HTTP API.
- The dispatch benchmark covers a synthetic single-tenant workload with two connections. Push-provider latency, sustained high-tenant load and staging capacity remain release work.
- The ready endpoint allows an isolated probe per request. Limit readiness ingress to intended probes and confirm connection capacity in staging. The delayed-cleanup test exercises the outer HTTP bound and releases its controlled dependency explicitly.

## 13. Independent re-audit readiness

| Verification command | Environment | Result |
| --- | --- | --- |
| `dotnet restore backend/FieldOps.slnx --locked-mode`, `dotnet build backend/FieldOps.slnx -c Release --no-restore`, `dotnet format backend/FieldOps.slnx --verify-no-changes --no-restore`, `dotnet ef migrations has-pending-model-changes` | Windows SDK 10.0.401, Linux container build used 10.0.403 and locked restore | Exit 0 for each. Release build had zero warnings and errors, and EF reported a matching model. |
| `dotnet test backend/FieldOps.slnx -c Release --no-build` | Windows execution against isolated PostgreSQL 18.6 in Linux Docker, local OTLP receiver | Exit 0. 92 unit and 174 integration tests passed, 266 total, zero skipped. |
| `dotnet test` filtered to `SocketUpgradeAndLifetime` and `ReadinessReturnsBounded` | Windows, real PostgreSQL and loopback OTLP | Exit 0, 2/2. The earlier test-first run exposed missing upgrade metrics, stale batch timestamps and lossy rollback. |
| `docker compose ... config --quiet` and `up -d --build --wait api` | Isolated Compose project | Exit 0, rebuilt Linux API, successful migration and healthy API. |
| `node infrastructure/scripts/phase7-benchmark.mjs --slow-download` | Windows client, Linux API and MinIO through a private Docker-network proxy | Baseline exit 1; remediated exit 0. Small and large downloads, two stalls, client disconnect, retry, source cleanup and tenant test passed. |
| `node infrastructure/scripts/phase7-benchmark.mjs --dispatch-load` | Windows client and isolated Linux API/PostgreSQL, two sockets | Exit 0 on both revisions. 180 measured events per revision, with 20 low-load and 160 burst samples. |
| `Invoke-WebRequest` while the isolated PostgreSQL container is paused | Windows HTTPS client and Linux Docker | Drill exit 0. Three bounded 503 responses, unaffected liveness and recovery to 200. |
| `pg_dump -Fc`, `pg_restore --exit-on-error`, and `dotnet ef database update` | New synthetic restore DB in isolated Linux PostgreSQL | Dump and restore exited 0. Expected guarded downgrade exited 1. Reapplication exited 0 and preserved schema history and delivery state. |
| `npm run mobile:lint`, `mobile:typecheck`, `mobile:test`, `mobile:check`, workspace `format:check`, and `npm run test:integration -w @fieldops/mobile` | Windows Node 24 with real local HTTPS API for integration | Exit 0 for each, 70 Vitest, 25 Jest and 10 HTTP integration cases. Expo dependency check passed. |
| `npm run web:lint`, `web:typecheck`, `web:test`, `web:build`, and `npm run format:check -w @fieldops/admin-web` | Windows Node 24 | Exit 0 for each. 62 Vitest passed. Entry remained 452.51 kB, 141.69 kB gzip, with no 500 kB build warning. |
| `npm exec -w @fieldops/admin-web -- playwright test --grep ...` | Windows Edge 157.0.4341.60 with the isolated HTTPS/PostgreSQL/MinIO/SignalR stack | Checklist and Schedule test-first runs exited 1 at the expected disabled-control assertion. With the final control changes, the focus, checklist and manager workflow tests passed, exit 0 (3/3). |
| `npm exec -w @fieldops/admin-web -- playwright test` | Same Edge and API, new fixture workspaces per run | First full run on the final code exited 0, 17/17. |

Verification output is in `artifacts/phase-7-gate/`. No commit, push or PR was made.
