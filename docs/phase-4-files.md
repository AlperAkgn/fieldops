# Phase 4 changed files

All paths are repository-relative. Existing health transport/tests and the Metro script have formatting-only changes to satisfy the new mobile formatter. Backend source/migrations, admin-web application source and AGENTS.md are unchanged. The web manifest adds only the compatible Ajv development peer needed for a valid complete dependency tree.

- .env.example
- .github/workflows/ci.yml
- apps/admin-web/package.json
- apps/mobile/.env.example
- apps/mobile/.prettierignore
- apps/mobile/.prettierrc.json
- apps/mobile/app.json
- apps/mobile/integration/technician.test.ts
- apps/mobile/jest.config.cjs
- apps/mobile/jest.setup.ts
- apps/mobile/package.json
- apps/mobile/scripts/smoke.mjs
- apps/mobile/src/api/contracts.ts
- apps/mobile/src/api/health.test.ts
- apps/mobile/src/api/health.ts
- apps/mobile/src/api/transport.test.ts
- apps/mobile/src/api/transport.ts
- apps/mobile/src/app/_layout.tsx
- apps/mobile/src/app/(tabs)/_layout.tsx
- apps/mobile/src/app/(tabs)/index.tsx
- apps/mobile/src/app/(tabs)/orders.tsx
- apps/mobile/src/app/(tabs)/profile.tsx
- apps/mobile/src/app/index.tsx
- apps/mobile/src/app/sign-in.tsx
- apps/mobile/src/app/work-orders/[id].tsx
- apps/mobile/src/auth/runtime.tsx
- apps/mobile/src/auth/session.test.ts
- apps/mobile/src/auth/session.ts
- apps/mobile/src/components/ui.tsx
- apps/mobile/src/features/mobile.spec.tsx
- apps/mobile/src/features/orders.test.ts
- apps/mobile/src/features/orders.ts
- apps/mobile/src/features/OrdersScreen.tsx
- apps/mobile/src/features/queries.ts
- apps/mobile/src/features/WorkOrderScreen.tsx
- apps/mobile/src/i18n/en.ts
- apps/mobile/tsconfig.json
- apps/mobile/vitest.integration.config.mts
- docs/architecture.md
- docs/implementation-status.md
- docs/mobile.md
- docs/phase-4-files.md
- docs/phase-4-results.md
- docs/security.md
- package-lock.json
- README.md

Generated Android code, export bundles, test outputs, fixture databases, local certificate and audit output are ignored local artifacts, not committed sources. The lockfile reflects pinned supported native dependencies and the native component-test toolchain.
