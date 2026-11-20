# FIELDOPS — PHASE 7 GITHUB ACTIONS CI REMEDIATION REPORT

Verified locally on 2026-11-20, branch `feat/security-observability`, starting from `8ab7936`. The existing hosted run used that commit. [Hosted run](https://github.com/AlperAkgn/fieldops/actions/runs/44193183423)

## 1. Web CI exact failure

The hosted web job failed at `npm audit --audit-level=high` with exit 1. Its audit reported 72 High and 13 Moderate affected dependency entries, including GHSA-fc5q-39xc-vjpf. The later lint, typecheck, tests and build steps did not run. The install's earlier summary reported 71 High entries. The explicit audit is the security gate's evidence. [Web job](https://github.com/AlperAkgn/fieldops/actions/runs/44193183423/job/134427642650)

## 2. picomatch advisory withdrawal verification

The official GitHub advisory and REST record confirm withdrawal at `2026-11-18T15:38:56Z` and an update at `2026-11-19T16:07:41Z`. GitHub states that the reported behavior is not a security issue. This supersedes the earlier P7-08 blocker decision. Resolution follows the official withdrawal and refreshed registry results. [GitHub advisory](https://github.com/advisories/GHSA-fc5q-39xc-vjpf) · [Official API record](https://api.github.com/advisories/GHSA-fc5q-39xc-vjpf)

## 3. Fresh npm audit results

The configured registry is `https://registry.npmjs.org/`.

| Command | Exit | Current result |
| --- | --- | --- |
| `npm audit --audit-level=high` | 0 | High-threshold security gate passes |
| `npm audit --json` | 1 | 30 Moderate, 0 High, 0 Critical affected entries |
| `npm audit --omit=dev --json` | 1 | 3 Moderate, 0 High, 0 Critical affected entries |

The withdrawn advisory ID is absent from the fresh full JSON. The remaining two distinct advisories are GHSA-3g3m-2fjr-6pw4 (`glob`) and GHSA-g3hv-75wg-m249 (`minimatch`). Counts include affected consumers and reflect severity propagation through the graph. Package versions, the lockfile, npm configuration and the CI audit command retained their original contents. No exception was introduced.

## 4. Docker CI exact failure

The hosted job successfully built the stack, applied migrations, reached healthy services and passed its HTTPS authentication smoke. Browser testing then finished 16/17. The checklist scenario timed out at `admin.spec.ts:1395` waiting 12,000 ms for the response to `POST /work-orders/{id}/pause`. The subsequent mobile HTTP integration step was skipped by GitHub after this failure. [Docker job](https://github.com/AlperAkgn/fieldops/actions/runs/44193183423/job/134427642959)

Authenticated artifact listing for that run returned zero artifacts. The workflow generated trace, screenshot and error-context files but had no upload step, so those hosted files could not be retrieved. Job logs were downloaded. Local reproduction artifacts supply the request and browser evidence below.

## 5. Playwright root cause

The unchanged checklist test passed in isolation. A deterministic diagnostic then delayed the real committed Start event for version 9 until after the independent owner committed version 10. The existing fixture held the version 10 event only. The older event still invalidated the detail query, whose real GET returned version 10. The open Pause dialog retained version 9. The existing client version guard rejected that command locally and kept the dialog open with the reload message.

The diagnostic reproduced the same 12,000 ms response timeout. Its trace showed GET version 9, owner PATCH version 10, subsequent GET version 10, and zero Pause POSTs. The screenshot showed the enabled Confirm pause button and the local conflict error. This establishes a reproducible fixture race matching the hosted symptom. The original hosted event ordering remains unavailable because its artifacts were not retained.

| Candidate | Evidence from the local reproduction |
| --- | --- |
| A. POST never sent | Confirmed, zero Pause POSTs |
| B. POST sent without response | No such request in the trace |
| C. Response predicate mismatch | No Pause response existed to match |
| D. Disabled form prevented interaction | The click completed and displayed the local conflict error |
| E. SignalR refetch raced with mutation | Older committed event supplied the newer detail version before submission |
| F. Fixture synchronization | Holding only the newest event failed to freeze the detail snapshot |
| G. Shared previous-test state | Isolated reproduction used its own workspace and order |

## 6. Code and test changes

The checklist test now deliberately delays the genuine version 9 event and gates detail refreshes before the independent version 10 change. It observes the pending GET and displayed version 9 before submission, then requires the real API 409, an enabled Confirm button and preservation of the Reason draft. The gate is released in `finally`. Explicit Reload obtains version 10 and the next user-initiated Pause submits that version and reaches version 11. Existing checklist write-version assertions remain.

The passing targeted trace records exactly two Pause requests, first `expectedVersion=9` with HTTP 409 / `work_order_version_conflict`, then `expectedVersion=10` with HTTP 200 / version 11. Production concurrency, mutation, focus and SignalR code retained their original contents. Test timeout values, retries and assertions retain their existing limits.

Changed files are `apps/admin-web/e2e/admin.spec.ts`, `docs/implementation-status.md`, the historical notice in `docs/phase-7-picomatch-decision.md`, and this report.

## 7. Regression verification

Verification uses the isolated Docker project `fieldops-p7-ci-20261120`, real PostgreSQL, HTTPS, Mailpit, MinIO and SignalR. Evidence is under ignored `artifacts/phase-7-hosted-ci/`.

| Verification | Result |
| --- | --- |
| Unchanged isolated checklist test | 1/1 passed |
| Controlled late-event diagnostic before fix | Failed with the hosted response-timeout signature |
| Fixed checklist test with retained passing trace | 1/1 passed |
| Related focus, Save, manager workflow and checklist cases | 6/6 passed |
| Full Edge invocation 1 | 17/17 passed, 4.0 minutes, one worker, zero retries/skips |
| Full Edge invocation 2 | 17/17 passed, 4.1 minutes, one worker, zero retries/skips |
| `npm run web:lint`, `npm run web:typecheck`, web `format:check`, `npm run web:build` | All passed |
| `npm run web:test` | 62/62 passed |
| Compose build, healthy services, migration and idempotent reapply | Passed on the isolated project |
| `smoke-web-auth.py` with the public CA and normal API rate limits | Trusted HTTPS, CORS, CSRF, SMTP registration/confirmation, login, refresh, logout and HTTP 429 passed |
| Mobile `test:integration` step from the Docker CI job | 10/10 passed with CA-validated HTTPS |

Backend and mobile sources were unchanged, so their comprehensive suites were not rerun. Their earlier hosted success remains historical evidence. Existing development data and Docker volumes were preserved.

## 8. Security gate result

**P7-08: RESOLVED. DEPENDENCY SECURITY GATE: PASS locally.** The unmodified high-threshold command exited 0 against the current registry. Default full and production-classified audit commands retain exit 1 for Moderate findings. Hosted results await a new run.

## 9. Remaining CI blockers

The current remediation has not run on GitHub Actions. The last hosted web and Docker jobs remain failed. Local browser verification uses installed Edge on Windows, while the hosted workflow uses Chromium on Ubuntu. The historical hosted trace is unavailable. Moderate npm findings and native validation `DEFERRED / NOT VERIFIED` remain recorded limitations.

## 10. Pull Request readiness

**WEB CI: BLOCKED pending hosted recheck.**

**DOCKER CI: BLOCKED pending hosted recheck.**

**P7-08: RESOLVED.**

**PHASE 7 HOSTED CI RECHECK READINESS: READY.**

The next gate is a new hosted run of the reviewed correction, followed by the independent Phase 7 decision. Phase 8 remains outside this task.
