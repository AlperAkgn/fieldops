# Phase 3 changed-file inventory

All paths are repository-relative. Source files below were created or changed for Phase 3; generated outputs, private database/key backups, browser traces and local configuration are ignored and excluded.

## Backend

- `backend/src/FieldOps.Api/Endpoints/ReportingEndpoints.cs`
- `backend/src/FieldOps.Api/Program.cs`
- `backend/src/FieldOps.Application/Reporting/IReportingReader.cs`
- `backend/src/FieldOps.Infrastructure/DependencyInjection.cs`
- `backend/src/FieldOps.Infrastructure/Operations/ReportingReader.cs`
- `backend/tests/FieldOps.IntegrationTests/ReportingTests.cs`

## Admin web

- `apps/admin-web/.prettierrc.json`
- `apps/admin-web/e2e/admin.spec.ts`
- `apps/admin-web/index.html`
- `apps/admin-web/package.json`
- `apps/admin-web/playwright.config.ts`
- `apps/admin-web/public/favicon.svg`
- `apps/admin-web/src/api/client.test.ts`
- `apps/admin-web/src/api/client.ts`
- `apps/admin-web/src/api/contracts.ts`
- `apps/admin-web/src/api/health.test.ts`
- `apps/admin-web/src/api/health.ts`
- `apps/admin-web/src/api/hooks.ts`
- `apps/admin-web/src/api/session.ts`
- `apps/admin-web/src/App.tsx`
- `apps/admin-web/src/auth.test.tsx`
- `apps/admin-web/src/auth.tsx`
- `apps/admin-web/src/components/ErrorBoundary.test.tsx`
- `apps/admin-web/src/components/ErrorBoundary.tsx`
- `apps/admin-web/src/components/form-schema.ts`
- `apps/admin-web/src/components/Form.test.tsx`
- `apps/admin-web/src/components/Form.tsx`
- `apps/admin-web/src/components/Layout.tsx`
- `apps/admin-web/src/components/OrderActions.tsx`
- `apps/admin-web/src/components/ui-state.ts`
- `apps/admin-web/src/components/ui.tsx`
- `apps/admin-web/src/i18n/en.ts`
- `apps/admin-web/src/main.tsx`
- `apps/admin-web/src/pages/AuthPage.tsx`
- `apps/admin-web/src/pages/catalog-fields.ts`
- `apps/admin-web/src/pages/CatalogPage.tsx`
- `apps/admin-web/src/pages/ManagementPages.tsx`
- `apps/admin-web/src/pages/OrderCollaboration.tsx`
- `apps/admin-web/src/pages/OverviewPage.tsx`
- `apps/admin-web/src/pages/SchedulingPage.tsx`
- `apps/admin-web/src/pages/WorkOrdersPage.tsx`
- `apps/admin-web/src/styles.css`
- `apps/admin-web/src/test-setup.ts`
- `apps/admin-web/tsconfig.json`
- `apps/admin-web/vite.config.ts`

## Documentation, dependencies and CI

- `.github/workflows/ci.yml`
- `docs/api.md`
- `docs/architecture.md`
- `docs/development-roadmap.md`
- `docs/implementation-status.md`
- `docs/phase-3-migration-plan.md`
- `docs/phase-3-results.md`
- `docs/security.md`
- `infrastructure/docker/compose.e2e.yml`
- `package-lock.json`
- `README.md`
- `docs/phase-3-files.md` (this inventory)

## Quality-gate remediation additions and updates

- New: `apps/admin-web/src/api/work-order-state.ts`, `apps/admin-web/src/api/work-order-state.test.tsx`, `apps/admin-web/src/components/WorkOrderError.tsx`, `apps/admin-web/src/pages/work-order-sync.test.tsx`, `apps/admin-web/src/pages/management-regression.test.tsx`, `docs/phase-3-quality-gate-remediation.md`.
- Updated: `apps/admin-web/src/components/Layout.tsx`, `apps/admin-web/src/components/ui.tsx`, `apps/admin-web/src/components/OrderActions.tsx`, `apps/admin-web/src/pages/WorkOrdersPage.tsx`, `apps/admin-web/src/pages/OrderCollaboration.tsx`, `apps/admin-web/src/pages/ManagementPages.tsx`, `apps/admin-web/src/styles.css`, `apps/admin-web/e2e/admin.spec.ts`, `apps/admin-web/playwright.config.ts`, `docs/architecture.md`, `docs/implementation-status.md`, `docs/phase-3-results.md`, and this inventory.
- No backend, migration, package or lockfile modification was required by remediation. Earlier uncommitted Phase 3 implementation changes remain in the workspace.
