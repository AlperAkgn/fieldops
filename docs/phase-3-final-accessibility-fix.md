# Phase 3 final accessibility fix

Date: 2026-11-08 (Europe/Istanbul). Single-agent execution. Scope: the remaining successful-save modal focus restoration defect only.

## Root cause

The work-order mutation invokes its successful-close callback after the API acknowledgement/refetch but before TanStack Query leaves its pending state. The Edit trigger is therefore still disabled when the modal's layout-effect cleanup calls `focus()`. Being connected to the DOM does not make a disabled element focusable. No later restoration occurred when React enabled the trigger, leaving focus on BODY. Escape works because no mutation is pending.

A new real-browser test reproduced this before the fix: PATCH returned 200, the dialog closed, the trigger became enabled, but `toBeFocused()` failed. The failing trace and log are preserved separately under ignored `artifacts/phase-3-final-focus/baseline` and `baseline.log`.

## Implemented fix

- Modal cleanup checks whether the focus attempt actually succeeded. An unavailable, disabled, inert or removed trigger receives a meaningful main-landmark fallback rather than leaving BODY focused.
- The work-order Edit dialog hands its captured return target to its owning page. A page layout effect waits until the dialog has closed and aggregate mutation/refetch activity has finished. It then restores focus after React has committed the trigger's enabled state.
- The page clears the pending return target and respects focus moved elsewhere by the user. A removed trigger retains the main-landmark fallback.
- Dialog close timing, mutation locking, validation, API/concurrency contracts, Escape and focus trapping remain intact. There are no new timers, arbitrary waits, observers, packages or backend changes.

## Files changed

- `apps/admin-web/src/components/ui.tsx`: optional focus-return handoff and verified fallback.
- `apps/admin-web/src/pages/WorkOrdersPage.tsx`: Edit-specific post-commit restoration.
- `apps/admin-web/src/components/Modal.test.tsx`: two new component regressions.
- `apps/admin-web/e2e/admin.spec.ts`: one new browser scenario; the existing eleven remain unchanged.
- This report, `docs/implementation-status.md`, and a historical-status note in `docs/phase-3-quality-gate-remediation.md`.

## Added regression tests

Two component tests cover a still-disabled connected trigger and a trigger removed by React during close. Native dialog methods are stubbed only in these DOM tests; actual dialog behavior is also exercised in Playwright.

The browser scenario uses the real HTTPS API/PostgreSQL and verifies all six required cases: successful Edit/PATCH 200/close/Edit focus; Escape/Edit focus; an actually pending write with a disabled trigger; a genuine second-actor API 409 preserving the open dialog and unsaved input; successful completion removing its trigger and focusing main; and Tab/Shift+Tab containment. A response gate delays delivery of an unchanged real API 200 to inspect pending state without a fixed sleep or timeout increase.

## Playwright focus results

The new scenario failed before the production fix and passed afterward, including all six cases. The complete suite passed **12/12 scenarios in one run (2.8 minutes), zero skipped/retries, one worker**: all existing eleven plus the new focus regression. Browser execution used local Microsoft Edge with the real HTTPS API/PostgreSQL/Mailpit stack. Evidence remains local under ignored `artifacts/phase-3-final-focus`, including `baseline.log`, `targeted-fixed.log`, `full-e2e.log` and separate HTML reports. Traces can contain disposable test session data and are not published.

## Web regression results

Web lint, strict TypeScript typecheck, Prettier verification and production build passed. **58/58 web tests passed**, retaining all **56 existing tests** and adding two. No check was disabled. Backend code/schema/contracts were not changed, so the 227-test backend suite was intentionally not rerun, as requested.

## Remaining issues

No remaining defect was found in the requested focus scope after the full regression. The existing approximately 503 kB main-bundle advisory remains visible and its threshold was not changed. Hosted CI, other browser engines and native environments were not exercised by this focused task. No backend tests, migrations or user-data deletion were performed.

## Phase 4 readiness

**READY for independent revalidation and separately authorized Phase 4 implementation.** All requested local focus, web and browser checks passed. No Phase 4 work was performed.
