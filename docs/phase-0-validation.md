# Phase 0 validation

Date: 2026-11-05 (Europe/Istanbul). Scope: revalidate and repair Phase 0 only. Phase 1 remains **Not Started** and requires explicit authorization.

## Result

**PHASE 1 READINESS: READY for the next authorized development phase.** This is not a production-readiness claim. All mandatory local Phase 0 acceptance checks pass after the repairs below. Native device execution, hosted GitHub Actions, and one additional service-interruption exercise remain unverified and are not marked as passed.

## Fourteen-point audit

| # | Check | Result and evidence |
| --- | --- | --- |
| 1 | Repository / monorepo | PASS: four backend projects, two npm workspaces, six NuGet lockfiles, npm lockfile, infrastructure, CI, and substantive docs. No Phase 1 implementation. Git is initialized locally; no commit or remote was created by this audit. |
| 2 | Architecture / dependencies | PASS: executable architecture tests validate the exact inward project references. Domain/Application have no package/framework references. No generic repository, mediator framework, Redis, or microservice was added. |
| 3 | ASP.NET Core 10 | PASS: Release build with zero warnings/errors on Windows SDK 10.0.401 and Linux SDK 10.0.403. Linux runtime image actually reports ASP.NET Core/.NET 10.0.14. |
| 4 | React Native / Expo | PASS for locked installation, dependency/peer compatibility, Router/Metro startup, Android manifest and native-platform JavaScript bundling. Expo Doctor 20/20. Native package/device execution is BLOCKED separately. |
| 5 | React admin web | PASS: clean install, lint, typecheck, 23 transport tests and production Vite build on Windows and fresh Linux. Browser verified real healthy cards and a safe invalid-config state. |
| 6 | TypeScript strictness | PASS: both effective compiler configurations are strict. Temporary negative probes rejected implicit any, unsafe null assignment, unknown catch access, and unsafe function variance in both projects. |
| 7 | PostgreSQL / Compose | PASS: configuration validation, no-cache API/MinIO builds, fresh isolated Compose volumes/network, successful bootstrap migration, three healthy services and a successful one-shot migration. Signed local MinIO PUT/GET succeeded. Main development volumes were preserved. |
| 8 | Health endpoints | PASS: actual HTTP 200/Healthy for live and ready on the default and fresh isolated stacks; failure/redaction/time-bound behavior verified by integration tests. API image readiness HEALTHCHECK is active. |
| 9 | EF / migrations | PASS: actual PostgreSQL fresh-database migration, idempotent second application, pending-to-ready transition, no pending model changes, host EF CLI update and container migration command. Only migration-history table exists. |
| 10 | GitHub Actions | PASS for SHA metadata resolution, actionlint 1.7.12, supported commands and local Linux execution of build/test/bundle steps; Docker smoke commands also executed locally. Hosted workflow execution is BLOCKED without a configured remote/run. |
| 11 | Git/environment hygiene | PASS for tested ignore rules, explicit local-only sample credentials, public-origin-only client config, registry integrity/metadata inspection and a limited source secret-pattern scan. No real credentials were printed or added to source. This is not a complete security certification. |
| 12 | README / technical docs | PASS: checked against actual scripts/routes/versions/phase boundaries, refreshed for repairs, and local Markdown targets validated. Planned features remain labelled as planned. |
| 13 | AGENTS / status | PASS: authoritative per-phase gate, English engineering docs, resume workflow, all nine phase statuses and actual verification/limitations recorded. |
| 14 | Dependencies / technical debt | PASS for the scope/architecture review: feature packages remain deferred, no unnecessary framework added, dependency tree has no peer problems, NuGet reports no advisories. The npm advisory findings and archived MinIO upstream remain disclosed below. |

## FIXED

### Client correctness and startup

- Both health transports previously coerced status values through `String()`, accepting malformed arrays, and accepted contradictory HTTP 503/Healthy responses. They now require the exact string union and consistent HTTP/status mapping. Already-aborted requests do not start network I/O.
- API URLs now accept only HTTP(S) origins with an optional trailing slash. Credentials, paths, queries and fragments are rejected; invalid raw configuration is not rendered or placed in query keys. Actual browser testing used synthetic invalid credentials and showed only a safe configuration message.
- Root npm wrappers now forward extra arguments to the workspace command correctly.
- Real Expo development startup failed although exports and Doctor passed: hoisted Expo tooling could not resolve the workspace-local Router. The same pinned Router version is now also a root development dependency, producing one shared installation. No package patch, unsupported version override, or typed-route disablement was used. [ADR 0001](adr/0001-architecture.md) explains the placement.
- Added a bounded portable `mobile:smoke` that starts its own Expo process, requests Metro status, an Android manifest and a JavaScript HTTP bundle, and cleans up its process tree. It passes on Windows and Linux.
- Vite dev/preview now bind to loopback by default. Backend Development CORS explicitly permits localhost/127.0.0.1 on ports 5173/4173; untrusted origins are denied and Production defaults remain empty.

### Backend correctness and diagnostics

- Exception handling could erase the correlation header and omit the request completion log. Middleware ordering and `OnStarting` now preserve correlation and completion logging, and ProblemDetails uses the same trace ID.
- Unsupported Accept headers could bypass the custom safe error writer. An always-safe JSON fallback now handles these failures without raw exception diagnostics. Both JSON and HTML Accept cases have HTTP/logging regressions.
- Development OpenAPI previously returned an empty `paths` object. Both real GET health operations now have typed response schemas and explicit readiness 200/503 responses. Tests inspect actual generated contracts.
- A real TCP black-hole test reproduced a roughly 30-second PostgreSQL handshake despite the configured health cancellation deadline. Probe-only connection/command timeouts are now bounded to 3 seconds; normal application EF options are unchanged. The regression passes within its deadline.
- Health responses include no-store/no-cache headers.

### Containers and CI

- Updated Docker builder to SDK 10.0.403 and final API runtime from 10.0.12 to 10.0.14, with verified image digests. CI uses SDK 10.0.403; `global.json` retains the compatible minimum and patch roll-forward for the installed host tooling. Microsoft's [support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) requires current servicing patches.
- Added a real API readiness Docker HEALTHCHECK using a small curl runtime dependency. Compose `--wait` now waits for actual API/database readiness.
- CI now exercises live Metro and a separate full Docker infrastructure smoke job, rather than validating Compose YAML alone. The job builds services, waits for health, probes endpoints, reapplies migrations and cleans its disposable volumes.
- README, deployment/API/architecture documentation, ADR, file manifest and implementation status reflect the resulting behavior and limits.

## Executed checks

| Command / exercise | Actual outcome |
| --- | --- |
| `dotnet tool restore` and locked solution restore | PASS |
| `dotnet build backend/FieldOps.slnx --configuration Release --no-restore` | PASS, 0 warnings/errors on Windows and Linux |
| `dotnet format backend/FieldOps.slnx --verify-no-changes --no-restore` | PASS on Windows and Linux |
| Full solution tests with `FIELDOPS_TEST_DATABASE` | PASS: 6 unit + 15 integration, 21 total, 0 skipped, on Windows and Linux with actual PostgreSQL |
| `dotnet ef migrations has-pending-model-changes` | PASS: none |
| `dotnet ef database update` | PASS against the local Compose database |
| NuGet transitive vulnerability audit | No advisories reported |
| `npm ci --no-fund --no-audit` | PASS: 670 installed packages on Windows and clean Linux |
| `npm run lint` / `npm run typecheck` | PASS for both applications on Windows and Linux |
| `npm test` | PASS: web 23 + mobile 23 on Windows and Linux |
| `npm run web:build` | PASS on Windows and Linux |
| Effective strict configuration / negative compiler probes | PASS on Windows for both applications |
| `npm run mobile:check` | PASS on Windows and Linux |
| `npx expo-doctor@1.21.6` | PASS: 20/20 on Windows |
| `npm run mobile:export` | PASS: Android and iOS Hermes JavaScript bundles on Windows and Linux |
| `npm run mobile:smoke` | PASS: real startup, manifest and Android HTTP bundle on Windows and Linux |
| npm dependency-tree inspection | 0 peer/dependency problems |
| `npm audit` | 14 moderate, 0 high, 0 critical; remaining advisories described below |
| `docker compose config --quiet` | PASS |
| `docker compose build --no-cache api` / `minio` | Both PASS |
| Fresh `docker compose -p fieldops-phase0-audit up -d --build --wait --wait-timeout 90` | PASS on separate ports and new volumes; migration exit 0, API/PostgreSQL/MinIO healthy |
| Fresh-stack live/ready HTTP probes | Both HTTP 200; ready reports PostgreSQL Healthy |
| Actual development OpenAPI | Contains `/health/live`, `/health/ready`, typed responses and readiness 200/503 |
| Actual CORS HTTP probes | All four explicit development origins permitted, unrelated origin denied |
| Signed isolated MinIO bucket/object PUT and object GET | PASS; no application attachment feature implied |
| Fresh database table inspection | Only `__EFMigrationsHistory`; no Phase 1 entities |
| Repeat container migration | PASS, exit 0 |
| API/MinIO identity inspection | Non-root users (1654 / 10001) |
| API runtime inspection | Actual ASP.NET Core and .NET 10.0.14 |
| SHA-pinned action metadata requests | All three official `action.yml` resources HTTP 200 |
| `actionlint` 1.7.12 | PASS after final workflow edits; downloaded official binary checksum verified |
| Source manifest / local documentation links | PASS: 85 eligible source files, complete manifest, all local Markdown link targets resolve |
| Local CI-step reproduction | PASS in fresh Linux Node 24.21.0/npm 11.19.0 and SDK 10.0.403 containers; not a hosted GitHub run |
| Browser at localhost:5173 | Both real health cards Healthy; refresh action exercised |
| Browser with synthetic invalid URL at localhost:5174 | Safe Unavailable/Invalid configuration state; raw synthetic credentials/path/token absent |

There are **67 distinct passing automated tests**, repeated successfully on Windows and Linux; repeated runs are not counted as additional distinct tests. Expo exports, manifest generation and JavaScript HTTP bundles are not native APK/IPA builds or device execution.

The fresh Docker test project used ports 15080/15432/19000/19001 and separate named volumes. Its signed object probe contained only synthetic text. Its containers, network and volumes were removed after inspection; the normal `fieldops` project and data volumes were preserved and updated to the final healthy images. The temporary invalid-configuration Vite process was also stopped; the normal server on 5173 remains available.

## BLOCKED

- **Hosted GitHub Actions execution:** the repository has no configured GitHub remote or run. Static workflow validation and local Linux command reproduction pass; no push or hosted execution is claimed.
- **Native Android/iOS execution:** no adb/emulator/standard Android SDK or Android Studio JBR was discovered, SDK environment variables are unset, and an iOS simulator is unavailable on this Windows host. No APK/IPA build, native signing or native-device run is claimed.
- **Additional deliberate service interruption:** automatic approval review rejected the compound command that would stop/restart the isolated PostgreSQL service, inspect Docker outage/recovery status, and restart MinIO to test persisted data. The tool's stated reason was `blocked by policy`; the command did not execute. Therefore this specific outage/restart-persistence exercise is not marked passed. Separate actual ASP.NET/PostgreSQL/black-hole integration tests cover unavailable connections, migration readiness transitions and bounded failure responses; these do not substitute for the blocked container interruption exercise.

## REMAINING

- npm reports 14 moderate propagated tooling findings from two underlying advisories: [minimatch GHSA-g3hv-75wg-m249](https://github.com/advisories/GHSA-g3hv-75wg-m249) and [glob GHSA-3g3m-2fjr-6pw4](https://github.com/advisories/GHSA-3g3m-2fjr-6pw4). Compatible upstream tool versions still constrain the affected major lines. No unsupported override or forced major downgrade was used. Recheck on dependency upgrades; do not interpret a passing build as a zero-advisory audit.
- MinIO's archived upstream remains a local-development maintenance exception; see [ADR 0002](adr/0002-local-object-storage.md). Reassess a maintained provider before Phase 6/production.
- The installed Windows SDK/runtime remains 10.0.401/10.0.12. Its actual compilation/tests pass, but it was not upgraded system-wide. The supported current runtime is verified in the Linux container; current SDK/runtime patches should be installed for future host deployments.
- Host export output noted simultaneous `NO_COLOR` and `FORCE_COLOR` environment variables. This nonfatal terminal-color warning did not affect builds or exports.
- No unresolved Phase 0 source/build/runtime defect remains. No production security certification, cloud deployment, hosted demo, identity, tenant isolation feature or offline capability is claimed.

## Next action

Stop at Phase 0. Phase 1 may begin only after the user explicitly requests it. Its planned identity/tenant work and acceptance tests are unchanged.

## Changed files

33 source/configuration/documentation files changed or were added during this audit. Ignored verification artifacts are excluded.

- [.env.example](../.env.example)
- [.github/workflows/ci.yml](../.github/workflows/ci.yml)
- [README.md](../README.md)
- [apps/admin-web/package.json](../apps/admin-web/package.json)
- [apps/admin-web/src/App.tsx](../apps/admin-web/src/App.tsx)
- [apps/admin-web/src/api/health.test.ts](../apps/admin-web/src/api/health.test.ts)
- [apps/admin-web/src/api/health.ts](../apps/admin-web/src/api/health.ts)
- [apps/admin-web/src/i18n/en.ts](../apps/admin-web/src/i18n/en.ts)
- [apps/mobile/.gitignore](../apps/mobile/.gitignore)
- [apps/mobile/package.json](../apps/mobile/package.json)
- [apps/mobile/scripts/smoke.mjs](../apps/mobile/scripts/smoke.mjs)
- [apps/mobile/src/api/health.test.ts](../apps/mobile/src/api/health.test.ts)
- [apps/mobile/src/api/health.ts](../apps/mobile/src/api/health.ts)
- [apps/mobile/src/app/index.tsx](../apps/mobile/src/app/index.tsx)
- [apps/mobile/src/i18n/en.ts](../apps/mobile/src/i18n/en.ts)
- [backend/src/FieldOps.Api/Errors/GlobalExceptionHandler.cs](../backend/src/FieldOps.Api/Errors/GlobalExceptionHandler.cs)
- [backend/src/FieldOps.Api/Health/HealthResponse.cs](../backend/src/FieldOps.Api/Health/HealthResponse.cs)
- [backend/src/FieldOps.Api/Health/HealthResponseWriter.cs](../backend/src/FieldOps.Api/Health/HealthResponseWriter.cs)
- [backend/src/FieldOps.Api/Program.cs](../backend/src/FieldOps.Api/Program.cs)
- [backend/src/FieldOps.Api/appsettings.Development.json](../backend/src/FieldOps.Api/appsettings.Development.json)
- [backend/src/FieldOps.Infrastructure/Health/PostgreSqlReadinessCheck.cs](../backend/src/FieldOps.Infrastructure/Health/PostgreSqlReadinessCheck.cs)
- [backend/tests/FieldOps.IntegrationTests/ExceptionHandlingTests.cs](../backend/tests/FieldOps.IntegrationTests/ExceptionHandlingTests.cs)
- [backend/tests/FieldOps.IntegrationTests/HealthEndpointTests.cs](../backend/tests/FieldOps.IntegrationTests/HealthEndpointTests.cs)
- [docs/adr/0001-architecture.md](../docs/adr/0001-architecture.md)
- [docs/api.md](../docs/api.md)
- [docs/architecture.md](../docs/architecture.md)
- [docs/deployment.md](../docs/deployment.md)
- [docs/implementation-status.md](../docs/implementation-status.md)
- [docs/phase-0-files.md](../docs/phase-0-files.md)
- [docs/phase-0-validation.md](../docs/phase-0-validation.md)
- [infrastructure/docker/Api.Dockerfile](../infrastructure/docker/Api.Dockerfile)
- [package-lock.json](../package-lock.json)
- [package.json](../package.json)
