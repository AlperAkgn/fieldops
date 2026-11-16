# PHASE 6 FINAL SAVE STABILITY FIX

Local verification on 2026-11-16 (Europe/Istanbul), branch `feat/realtime-notifications`. The independent audit confirmed M1–M5 and the keyboard focus fix. It reported an intermittent Save failure in the full browser suite.

## 1. Root Cause

The Edit form used the work-order `busy` flag, which combines aggregate mutations and detail query fetching. When a SignalR invalidation started a GET between mouse down and mouse up, the form set the native Save button to disabled. The browser suppressed the click and sent no PATCH. The work order being edited kept the same server version throughout this reproduction. [HTML form control behavior](https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#enabling-and-disabling-form-controls:-the-disabled-attribute)

## 2. Application Bug vs Test Synchronization

The existing focus test passed when run against the original implementation in isolation. The new test held a real read open after an independent owner updated a different order, then released the pointer on Save. With the original code it observed a disabled button and zero browser PATCH requests. The test fixtures, request route and server version were available at the time of the click.

The existing server-409 scenarios also depended on when the new version reached the browser. A browser trace showed a detail GET followed by the client's stale-version message and no PATCH, despite the event for that version being held. A separate query could already supply the new version. The scenarios now release both the actual detail responses and the committed SignalR event after the API 409 and Escape. The application retains both the local and server safeguards.

The first full run after the Edit fix passed 17/17. The second passed the Save and focus scenarios and finished 16/17. Its manager workflow trace showed a missing checklist-apply POST followed by successful Start and Complete requests, because no required checklist had been applied. Apply checklist used the same combined `busy` flag. The workflow now holds an actual detail read during application and waits for the apply response and required item before continuing.

## 3. Implemented Fix

The Edit and Apply checklist forms now use aggregate write state. Live reads leave those actions interactive. Active writes still lock both forms, including the mutation's reconciliation. On submission the existing coordinator cancels related in-flight reads and checks the submitted version. Edit continues to use the exact `expectedVersion` captured when editing began. PostgreSQL-backed API validation rejects a concurrent change that the client has yet to observe. The draft stays in the open modal on conflict.

## 4. Files Changed

- `apps/admin-web/src/pages/WorkOrdersPage.tsx`
- `apps/admin-web/src/pages/OrderCollaboration.tsx`
- `apps/admin-web/src/api/work-order-state.test.tsx`
- `apps/admin-web/e2e/admin.spec.ts`
- This report, `implementation-status.md`, `phase-6-results.md` and `phase-6-final-focus-fix.md`

## 5. Added/Updated Regression Tests

The new Playwright test gates real WebSocket and HTTP traffic around a native Save click. It verifies exactly one browser PATCH and its opening version, includes two quick clicks during a pending write, and checks that a committed update seen during the write survives the earlier PATCH acknowledgement. A further live update after save verifies current data and Edit focus. The focus test opens the editor while its live GET is still held and checks that its fields and Save remain available. The work-order coordination test verifies that a late read cannot overwrite the acknowledged version. The existing browser conflict test uses controlled delivery of the genuine event.

## 6. Full Playwright Results

The focused verification passed the Save and checklist scenarios and both server-409 focus scenarios. All 12 focused Vitest cases passed. Linux lint, strict TypeScript, formatting, production build and the full 62-case Vitest suite passed.

Two separate final Edge invocations passed **17/17 each**, in 4.2 and 4.4 minutes. Each used fresh browser fixtures on the isolated stack, one worker and zero retries or skips. The existing 16 scenarios and the new Save regression ran in both. Earlier failed runs and final verification logs and HTML reports are preserved under ignored `artifacts/phase-6-final-save/`.

## 7. API & Concurrency Verification

The tests use independent authenticated Owner and Manager sessions on the isolated HTTPS API with real PostgreSQL and SignalR. The browser sees real HTTP 200 for a valid save and real HTTP 409 for a stale save. The conflict keeps unsaved input intact. The local known-version rejection and same-tick shared write lock are also exercised in the unit suite.

## 8. Focus Regression Verification

The suite exercises background GET focus, conflict and Escape, focus restoration after a successful save, a remote cancellation removing Edit, and a deliberate Tab to a different control while saving. The live collaboration and reconnect cases verify M1. The web SignalR, query coordinator and dialog code match the previous verified snapshot. M2–M5 remain covered by the earlier backend and mobile evidence. [Phase 6 results](phase-6-results.md)

## 9. Remaining Issues

Native Android/iOS acceptance remains `DEFERRED / NOT VERIFIED`. Verification used the local isolated stack and preserved the development database and Docker volumes. The existing 568 kB Vite bundle advisory remains. Hosted CI and native acceptance are outside the locally verified results.

## 10. Phase 7 Readiness

- **SAVE REGRESSION: RESOLVED**
- **WEB REGRESSION: PASS**
- **INDEPENDENT RE-AUDIT READINESS: READY**
- **PHASE 7 READINESS: CONDITIONAL**

Phase 7 requires the targeted independent re-audit and separate authorization.
