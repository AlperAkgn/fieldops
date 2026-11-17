# Phase 7 final quality gate remediation

## 1. Executive Summary

The supplied independent re-audit confirmed P7-01, P7-02, P7-03, P7-05, P7-06 and P7-07. This pass covers P7-04, P7-08 and P7-09 on `feat/security-observability`. It moves the readiness deadline to request entry, aligns the six published Expo SDK 58 patches, and preserves the failing npm security gate for the unpatched picomatch High advisory.

## 2. P7-04 Root Cause

The instrumented baseline full run reproduced the reported result, 265/266. The test client took 5,125.1 ms to receive the readiness response. The endpoint armed its 4,500 ms timer after the request traversed the cold request pipeline. The slow dependency started approximately 600 ms after server entry. Therefore the real caller observed over five seconds even though cancellation and HTTP writing proceeded quickly after the timer fired.

PostgreSQL was outside this particular fixture, which replaces the registered health check with a controlled delayed cleanup. The server callback committed headers before the client completed, and fixture cleanup was released only after completion. The initial 672.7 ms host setup also occurred before the stopwatch. These observations rule out database acquisition and post-response cleanup as causes of this failure. At cancellation there were ten pending ThreadPool items, and the cancellation occurred about nine milliseconds beyond the nominal budget measured from dependency entry. This run identifies cold pipeline time outside the old budget. It does not isolate the cost of individual middleware or assert ThreadPool starvation.

## 3. Readiness Runtime vs Test Timing

| Milestone | Baseline trace (ms from fixture start) |
| --- | ---: |
| Fixture ready | 672.7 |
| HTTP request issued | 674.3 |
| Request reached server | 676.0 |
| Controlled dependency entered | 1,276.0 |
| Cancellation observed | 5,785.5 |
| Response headers committed | 5,789.2 |
| HTTP response completed | 5,799.3 |
| Cleanup released / finished | 5,800.2 / 5,801.6 |

The existing 4.5-second response budget now starts before routing and middleware dispatch. The endpoint uses that same token, avoids starting a check on an expired budget, and returns 503 while cancelled dependencies unwind. The PostgreSQL connection, command and shared dependency budgets keep their existing values. The slow-cleanup regression retains the five-second end-to-end HTTP assertion, verifies cancellation and unfinished cleanup at response completion, then awaits cleanup and logs fixture disposal. The silent TCP fixture separately records database-side TCP acceptance and receipt of the full HTTP response. A real PostgreSQL fixture exhausts the EF request pool, verifies that readiness can still use the separate probe connection, and proves the pool can lease another connection after release.

The isolated Docker HTTPS probe returned 200 in 47.3 ms while PostgreSQL was healthy and 200 in 32.5 ms on liveness while PostgreSQL was paused. Three readiness requests returned 503 in 2,045.8, 2,037.2 and 2,048.1 ms. Readiness returned 200 in 35.8 ms after resume. The curl records distinguish TCP, certificate-verified TLS, first byte and full response. In this dependency failure, server time was dominated by the existing PostgreSQL connection budget. The pool and delayed-cleanup test cases cover distinct paths.

| Final timing in the concurrent backend suite | Run 1 | Run 2 |
| --- | ---: | ---: |
| Readiness dependency entry after server entry | 752.2 ms | 944.6 ms |
| Slow-cleanup HTTP request through full response | 4,550.8 ms | 4,622.5 ms |
| Slow-cleanup scope finished after HTTP completion | 10.3 ms | 6.3 ms |
| Silent PostgreSQL listener TCP accept after request | 701.8 ms | 732.0 ms |
| Silent PostgreSQL full HTTP response | 2,714.3 ms | 2,747.0 ms |

Both passing full runs incurred greater pre-dependency latency than the failing baseline. Moving the budget earlier absorbed that delay without shortening what the client stopwatch measures. The test logs include host initialization and final disposal separately.

## 4. P7-08 Dependency Inventory

`npm audit --json` and `npm audit --omit=dev --json` both exited 1 before and after the Expo update. The full tree reported 72 High and 13 Moderate entries. The production-classified tree reported 33 High and two Moderate entries. These are affected package nodes or aggregates. The full JSON contained three distinct direct advisories and only one High advisory, `GHSA-fc5q-39xc-vjpf`. NuGet listed zero affected packages.

The graph installs picomatch 4.0.7 and 2.3.2. FieldOps manifests do not depend on it directly. `npm ls picomatch --all`, `npm ls picomatch --omit=dev --all`, `npm explain picomatch --json` and the lockfile show the principal paths below.

| Path | Where it executes |
| --- | --- |
| Admin `vite` → picomatch 4.0.7 | Node dev server and build, including repository-owned pattern handling. |
| Root `vitest` → picomatch and `tinyglobby` → `fdir` / picomatch 4.0.7 | Node test discovery and file scanning. |
| Mobile `jest` / `jest-expo` → Jest utilities and test discovery, plus `@react-native/jest-preset` → Jest 29 utilities / `micromatch` → picomatch 2.3.2 | Component-test configuration and file discovery. This includes `anymatch` and Jest Haste Map on development paths. |
| Mobile `expo` → `@expo/cli` / `@expo/metro-config` → picomatch 4.0.7 | Expo CLI and Metro bundling. |
| Mobile `expo` → `@expo/cli` → `@expo/metro-file-map` → `jest-worker` → Jest 29 util → picomatch 2.3.2 | Metro file-map worker. |
| Mobile `expo` → `@expo/metro` → `metro-file-map` → `micromatch` → picomatch 2.3.2 | Metro file discovery. |
| Mobile `react-native` → `tinyglobby` / `fdir` → picomatch 4.0.7 | React Native toolchain file scans. |

## 5. P7-08 Exploitability Assessment

FieldOps source and build-configuration review found no application import of these glob parsers and no path that feeds API, search, note, or attachment inputs into toolchain glob configuration. The production Vite chunk inventory contained 447 modules and zero modules from picomatch, micromatch or anymatch. The Android and iOS Metro source maps contained 1,357 and 1,363 sources respectively, with zero matching parser modules on either platform. The Node tools still execute during dependency-backed developer, CI and release builds. Production classification in npm describes dependency reachability in the installation graph and includes that build tooling.

This bounds the demonstrated exposure to tooling. It does not remove the High advisory. A build system that accepts unreviewed patterns, configuration, CLI inputs or repository content has a different trust boundary. No attack traffic was sent to a deployed service.

## 6. Official Patch Availability

As checked on 18 November 2026, the GitHub advisory lists versions through 4.0.7 as affected and no patched version. The latest upstream tagged release and npm dist-tag are 4.0.7. The reviewed Expo SDK 58 patch set, Vite and Jest release information do not supply a verified advisory fix for these locked paths. [GitHub advisory](https://github.com/advisories/GHSA-fc5q-39xc-vjpf) · [Upstream releases](https://github.com/micromatch/picomatch/releases) · [npm dist-tag](https://registry.npmjs.org/picomatch/latest)

## 7. Security Mitigation / Remaining Risk

The repo's High/Critical gate remains enforced. The build keeps trusted, repository-owned glob inputs and the shipped bundles are checked separately. Removing these indirect paths would require replacing or materially changing supported Expo/Metro, React Native, Vite or test tooling. The evidence does not support overriding the package to an unpatched release or an unverified fork.

If an owner needs a temporary exception proposal, scope it to this advisory and reviewed, isolated developer/CI verification on trusted inputs. Set expiry to seven days or the first verified compatible upstream fix, whichever comes first. Keep it outside production release approval and require an explicit owner decision with an exact advisory and dependency inventory. This proposal is unapproved and no waiver, ignore or bypass was added.

## 8. P7-09 Expo Compatibility

Before alignment, `expo install --check` exited 1 and Doctor passed 19/20 checks. The exact six requested releases were present in the npm registry with versioned tarballs. The update aligns root and mobile Expo Router declarations and preserves React Native 0.88.1 and the existing SDK major. [Expo SDK dependency alignment](https://docs.expo.dev/workflow/upgrading-expo-sdk-walkthrough/) · [SDK 58 bundled module definitions](https://github.com/expo/expo/blob/sdk-58/packages/expo/bundledNativeModules.json)

| Package | Before | After |
| --- | --- | --- |
| `expo` | 58.0.25 | 58.0.26 |
| `expo-dev-client` | 58.0.20 | 58.0.21 |
| `expo-file-system` | 58.0.13 | 58.0.14 |
| `expo-router` | 58.0.31 | 58.0.32 |
| `expo-sqlite` | 58.0.17 | 58.0.18 |
| `jest-expo` | 58.0.14 | 58.0.15 |

The clean install now passes Expo compatibility and Doctor 20/20. Android and iOS JavaScript exports and Metro startup passed. Local `expo config --type introspect --json` exited 0 for the Android validation configuration. It resolved SDK 58.0.0 and `com.fieldops.mobile` on both platforms, with `android:usesCleartextTraffic=false` in the generated manifest. Both EAS Android profiles still select SDK 58 and `buildType=apk`. Physical Android/iOS and hosted EAS builds remain `DEFERRED / NOT VERIFIED` and `NOT VERIFIED`, respectively. [EAS SDK 58 image alias](https://docs.expo.dev/build-reference/infrastructure/#android-server-images)

## 9. Files Changed

- `backend/src/FieldOps.Api/Program.cs` and `Health/ReadinessDeadline.cs` arm the existing response budget at request entry.
- `HealthEndpointTests.cs` and `PostgreSqlMigrationTests.cs` capture timing evidence and exercise cancellation, slow cleanup, silent TCP and application-pool exhaustion with recovery.
- `OtlpReceiver.cs`, `DeploymentSecurityTests.cs` and `CollaborationTests.cs` accumulate every test export batch. In a concurrent full run the latest-only receiver had overwritten the batch with the expected trace. Assertions still require the route and socket metrics and now check redaction across all exports.
- `package.json`, `apps/mobile/package.json` and `package-lock.json` align SDK 58. CI YAML and EAS profiles are unchanged by this pass.
- The Phase 7 documentation and this report track current gates. See the [file inventory](phase-7-files.md#final-quality-gate-remediation).

## 10. Lockfile Changes

The lockfile refresh updates 20 package entries and adds or removes no package paths. Six are the listed direct patches. Associated updates are Expo CLI, Router server, Metro config, Expo Babel preset and dev launcher, plus the `noxcturnal` package and its eight platform-specific optional binaries. Picomatch instances and version ranges are unchanged. `npm ci` and `npm ls --all --json` verify the exact tree.

## 11. Test Results

Local evidence is kept under `artifacts/phase-7-final/`. The baseline `dotnet test` exited 1 on readiness. The first corrected run passed all 267 tests. A later concurrent run passed the readiness cases and exposed the test receiver's overwritten batch. That run also hit a socket-metrics flush timeout and a transient connect timeout in the legacy-session migration test while other local verification was active. Backend verification reran without changing their budgets or skipping tests.

| Command or check | Environment and result |
| --- | --- |
| `dotnet restore backend/FieldOps.slnx --locked-mode`, Release build, `dotnet format ... --verify-no-changes` | Windows .NET 10.0.401, exit 0. Final Release build had zero warnings and errors. |
| Targeted `dotnet test` for health/pool and OTLP/socket checks | Windows and isolated Docker PostgreSQL 18.6, 16/16 readiness and pool cases and 2/2 OTLP cases, both exit 0. |
| `dotnet test backend/FieldOps.slnx -c Release --no-build` | Two separate final processes against test-owned PostgreSQL databases. Both exit 0 with 92 unit and 175 integration tests, 267 total, zero skips. Includes migration, outbox, tenant isolation, identity and attachment cases. |
| Compose `config --quiet`, `up -d --build --wait api`, and certificate-verified `curl` probes | Isolated Linux containers, exit 0. Healthy 200, three bounded 503 responses, independent liveness and recovery 200 verified. |
| `node infrastructure/scripts/phase7-benchmark.mjs --slow-download` | Isolated MinIO and real HTTPS API, exit 0. A pre-header stall returned 503 in 8,010 ms. The partial body was interrupted in 8,023 ms, the upstream closed on client disconnect, and retry matched all bytes. Tenant denial returned 404. |
| `npm ci` and `npm ls --all --json` | Windows Node 24.21.0 / npm 11.19.0, exit 0. The installed tree matches the workspace lock. |
| Web lint, strict typecheck, format, `npm run web:test`, and `npm run web:build` | Windows, exit 0 for each. 62 Vitest tests passed and main JavaScript remained 452.51 kB. |
| `npm exec -w @fieldops/admin-web -- playwright test` | Windows Edge and the final isolated HTTPS API, exit 0, 17/17. Covers SignalR, focus, real 409s, checklist and scheduling controls, and private downloads. |
| Mobile lint, strict typecheck, format, `npm run mobile:test`, and `npm run mobile:check` | Windows, exit 0 for each. 70 Vitest and 25 Jest tests passed. |
| `npm run test:integration -w @fieldops/mobile` | Windows, isolated certificate-verified HTTPS API, exit 0, 10/10. |
| `npx expo-doctor`, `npx expo config --type introspect --json`, `npm run export -w @fieldops/mobile -- --source-maps`, `npm run mobile:smoke` | Windows and Expo SDK 58, exit 0. Doctor 20/20, configuration introspection, both JavaScript exports, and Metro Android bundle passed. |
| `npm audit --json`, `npm audit --omit=dev --json`, `npm audit --audit-level=high`; NuGet vulnerability listing and workflow `actionlint` | Local registry and toolchain. Each npm audit process exited **1**, with High findings as recorded above. NuGet and workflow syntax checks exited 0, with zero NuGet advisories. |

## 12. CI Security Gate Status

`.github/workflows/ci.yml` still runs `npm audit --audit-level=high` after `npm ci` in the client workflow. The same local gate exited **1** with the High advisory. Expo's compatibility check remains enabled. Hosted GitHub Actions is `NOT VERIFIED`.

## 13. Remaining Blockers

P7-08 remains open pending a compatible official fix. Security gate failure blocks the final independent readiness decision and Phase 8. Staging, hosted CI, EAS and physical devices are tracked separately and remain unverified.

## 14. Independent Re-Audit Readiness

| Decision | Status |
| --- | --- |
| P7-04 | **RESOLVED** |
| P7-08 | **BLOCKED** |
| P7-09 | **RESOLVED** |
| Backend regression | **PASS** |
| Web regression | **PASS** |
| Mobile regression | **PASS** |
| Dependency security gate | **FAIL** |
| Expo compatibility | **PASS** |
| Independent re-audit readiness | **NOT READY** |
| Phase 8 readiness | **NOT READY** |

P7-08 prevents a READY decision while the dependency gate fails. Prior independently resolved findings were covered by regression checks. Native device acceptance remains `DEFERRED / NOT VERIFIED`. Hosted CI, staging and native EAS builds remain `NOT VERIFIED`.
