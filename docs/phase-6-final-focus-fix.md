# PHASE 6 FINAL FOCUS REGRESSION FIX

Local verification on 2026-11-15 (Europe/Istanbul), branch `feat/realtime-notifications`. The supplied independent re-audit confirmed M1–M5 and reopened the gate for one Medium Edit focus regression.

The subsequent audit confirmed the focus fix and found a Save race in the Edit form's read/write guard. The follow-up changed that form to use the aggregate write guard. [Final Save stability fix](phase-6-final-save-stability-fix.md)

## 1. Root Cause

The detail page combined `useIsFetching` and `useIsMutating` in the flag that disabled Edit. A SignalR invalidation therefore disabled the focused button during an ordinary detail GET. Chromium moved focus to `BODY`. This also happened when Escape had successfully returned focus after a real 409. The previous post-commit restore cleared its stored target before checking whether `focus()` had succeeded.

## 2. Implemented Fix

Edit remains enabled during a background read and becomes disabled for aggregate writes, including their authoritative reconciliation. The Edit form keeps the existing read/write submission guard and frozen `expectedVersion`. The shared lock, cached-version rejection and backend 409 behavior remain in force.

The modal passes a target to the page only when its first restoration fails. The page retries after React commits and keeps the target until focus lands, the trigger is removed or the user moves to a different control. If a live update removes the focused Edit trigger, the stable React ref cleanup lets the page focus the main landmark after the commit. This uses the existing dialog and adds no timers. [React ref cleanup](https://react.dev/reference/react-dom/components/common#ref-callback)

## 3. Files Changed

- `apps/admin-web/src/api/work-order-state.ts` and `work-order-state.test.tsx`
- `apps/admin-web/src/pages/WorkOrdersPage.tsx`
- `apps/admin-web/src/components/ui.tsx`
- `apps/admin-web/e2e/admin.spec.ts`
- This report, `implementation-status.md`, `phase-6-results.md` and `phase-6-quality-gate-remediation.md`

## 4. Added Regression Tests

Two Edge Playwright scenarios cover a focused Edit button during a live GET and the same sequence after a genuine conflict and Escape. They also exercise fallback when a remote cancellation removes Edit and preserve a deliberate Tab to **All work orders** during a pending save. One Vitest scenario verifies that a held detail read keeps the form guard active without reporting an aggregate write. The existing browser and unit tests still cover successful save, write locking, double submission, draft preservation and Tab/Shift+Tab containment.

## 5. Real WebSocket/Playwright Results

The browser used an authenticated manager while a separate authenticated owner wrote to the same PostgreSQL-backed work order. Playwright forwarded real WebSocket frames and held the selected event for the conflict case until after Escape. It also delayed the response of the real detail GET to inspect focus while the query was active. No response payload was synthesized. Both new cases reported `BODY` focus before the fix and passed after it. The existing successful-save focus case passed in the targeted run. [Playwright WebSocket routing](https://playwright.dev/docs/api/class-websocketroute)

All **16/16 Edge Playwright scenarios** passed in 3.8 minutes, including the 14 existing cases. The local baseline, targeted and full reports are under ignored `artifacts/phase-6-final-focus/`.

## 6. Accessibility Results

Edit retains keyboard focus through the GET and its commit. A stale save returns 409 with the modal and draft intact. Escape returns to Edit. A successful save restores Edit after the write lock is released. When a remote cancellation removes Edit, focus moves to `main`. A user-selected control keeps focus after a pending save completes.

Linux web lint, strict TypeScript, format and production build passed. All 61 Vitest cases passed, including 11 focused dialog and work-order coordination cases.

## 7. Previous M1–M5 Regression Status

The M1 live comments/checklist/audit and reconnect/history scenario passed. The web SignalR hook and the mobile SignalR, token concurrency, attachments and workspace calendar sources match the previously verified Linux snapshot. The backend ticket reconciliation and retry rules remain in place. The independent re-audit confirmed M1–M5. Their prior backend and mobile suite evidence remains in the [M1–M5 remediation report](phase-6-quality-gate-remediation.md).

## 8. Remaining Issues

Physical Android/iOS acceptance remains `DEFERRED / NOT VERIFIED`. This run used the existing isolated HTTPS/PostgreSQL/WebSocket environment and disposable browser fixtures. The default development database and volumes were preserved. The 568 kB main web bundle still triggers the existing advisory. Hosted CI and the remaining deployment acceptance checks are tracked in the M1–M5 report.

## 9. Phase 7 Readiness

- **FOCUS REGRESSION: RESOLVED**
- **INDEPENDENT RE-AUDIT READINESS: READY**
- **PHASE 7 READINESS: CONDITIONAL**

The targeted independent focus re-audit is the next gate. Phase 7 requires separate authorization.
