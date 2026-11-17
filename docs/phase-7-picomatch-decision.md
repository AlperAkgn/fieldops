# FIELDOPS — P7-08 SECURITY BLOCKER DECISION REPORT

Investigation date 2026-11-18. Branch `feat/security-observability`.

## 1. Dependency tree

`npm ls picomatch --all`, `npm explain picomatch` and `npm ls picomatch --omit=dev --all` all exited 0. The root, admin and mobile manifests do not declare picomatch directly.

| Workspace dependency | Representative path | Execution context |
| --- | --- | --- |
| Admin `vite` 8.3.6 | `picomatch` 4.0.7 | Node dev server, file watching and production build |
| Root `vitest` 5.1.0 | `picomatch` 4.0.7, also through `tinyglobby` and `fdir` | Node test discovery and file scanning |
| Mobile `jest` 30.5.2, `jest-expo` 58.0.15 and React Native Jest preset | Jest utilities, `anymatch` and `micromatch` reach one or both affected lines | Node test setup and discovery |
| Mobile `expo` 58.0.26 | `@expo/cli` and `@expo/metro-config` → `picomatch` 4.0.7 | Expo CLI and Metro build configuration |
| Mobile `expo` 58.0.26 | `@expo/cli` → `@expo/metro-file-map` → `jest-worker` → `jest-util` → `picomatch` 2.3.2. Also `@expo/metro` → `metro-file-map` → `micromatch` → `picomatch` 2.3.2. | Metro file discovery and workers |
| Mobile `react-native` 0.88.1 | `tinyglobby` / `fdir` → `picomatch` 4.0.7 | Node filesystem tooling |

The first three rows represent development and test entry points. Expo and React Native are production-classified dependencies in npm, so their tooling paths remain in an `--omit=dev` graph.

## 2. Affected package versions

The lockfile contains 4.0.7 at `node_modules/picomatch` and three copies of 2.3.2 under `jest-util`, `micromatch` and `anymatch`. Only the `anymatch` copy is dev-only. Both versions fall within the advisory's `<=4.0.7` range. [GitHub security advisory](https://github.com/advisories/GHSA-fc5q-39xc-vjpf)

## 3. Production exposure

| Locally rebuilt target | Inventory examined | picomatch, micromatch or anymatch entries |
| --- | --- | --- |
| Admin web production build | 447 distinct emitted modules from the Vite chunk graph | 0 |
| Android Expo JavaScript export | 1,357 source entries from the generated Hermes source map | 0 |
| iOS Expo JavaScript export | 1,363 source entries from the generated Hermes source map | 0 |

The ASP.NET Core image publishes `backend/` into a .NET runtime image and starts `FieldOps.Api.dll`. The Dockerfile and backend source review found no npm dependency at runtime. The client evidence covers the inspected exports. Physical Android validation remains `DEFERRED / NOT VERIFIED`.

## 4. User-controlled input reachability

The application source and configuration review found no path from FieldOps requests, search values, notes or uploaded file names to the Node glob parser. Attachment names are validated and used as metadata and download labels. Storage keys derive from tenant, work-order and attachment IDs. The reachable parsing paths are workspace file discovery, build configuration and test tooling. Repository inputs and build configuration from outside a trusted checkout belong to that separate execution boundary.

## 5. Official patch status

The advisory lists no patched version. The latest published release on GitHub and the npm `latest` tag both identify 4.0.7. The npm 2.x line ends at 2.3.2. The upstream PR cited by the advisory was open and unmerged at verification. [Advisory](https://github.com/advisories/GHSA-fc5q-39xc-vjpf) · [GitHub releases](https://github.com/micromatch/picomatch/releases) · [npm registry metadata](https://registry.npmjs.org/picomatch) · [Upstream PR](https://github.com/micromatch/picomatch/pull/194)

## 6. Evaluated alternatives

| Option | Finding |
| --- | --- |
| Upgrade to a security release | No published fixed release was available for either installed line. A cross-major override would bypass consumer compatibility ranges. |
| Remove an unnecessary dependency | No direct picomatch declaration exists. Removing optional test or discovery paths leaves the Expo, React Native and bundler paths. |
| Use a supported replacement | Node's filesystem glob API can serve first-party file discovery. Expo/Metro, Jest and Vite use their own dependency APIs, and this investigation found no supported substitution for those call sites. |
| Upgrade a parent package | Vite 8.3.8 still requests `picomatch ^4.0.7`. The latest checked Vitest and Jest releases and the SDK 58 Expo, CLI, Metro configuration and React Native lines retain affected paths. |
| Restrict untrusted patterns | The application has no identified input path to restrict. Isolating Node jobs and controlling checkout and build configuration reduces tooling exposure while leaving the dependency finding open. |
| Keep the parser out of production | The emitted client inventories already show this separation. An `--omit=dev` install retains Expo/Metro tooling, and its audit still fails. |

[Vite 8.3.8 release](https://github.com/vitejs/vite/releases/tag/v8.3.8) · [Expo SDK 58 changelog](https://github.com/expo/expo/blob/sdk-58/packages/expo/CHANGELOG.md) · [Node filesystem glob API](https://nodejs.org/docs/latest-v24.x/api/fs.html#fspromisesglobpattern-options)

## 7. Applied safe changes

Updated this report and `implementation-status.md`. Application code, manifests, the lockfile and the CI workflow remain at their pre-investigation contents. Existing regression results remain recorded in the final Phase 7 remediation report. This investigation reran the release bundles and dependency checks. [Final quality gate remediation](phase-7-final-quality-gate-remediation.md)

## 8. npm audit results

| Command | Exit | Finding |
| --- | --- | --- |
| `npm audit --json` | 1 | 72 High, 13 Moderate, 0 Critical affected graph entries |
| `npm audit --omit=dev --json` | 1 | 33 High, 2 Moderate, 0 Critical affected graph entries |
| `npm audit --audit-level=high` | 1 | The CI threshold fails on the picomatch advisory |

The High counts are propagation of one advisory, GHSA-fc5q-39xc-vjpf. The full graph also returned two Moderate advisories, for `glob` and `minimatch`. The production-classified graph returned the `minimatch` Moderate advisory.

## 9. CI security gate

The web matrix leg runs the full workspace gate after `npm ci` and before client regressions. The exact command failed locally. A new GitHub Actions run was not triggered for this investigation. [CI workflow](../.github/workflows/ci.yml)

## 10. Remaining risk

The advisory still represents an availability risk for Node tooling if untrusted inputs reach the parser. CI timeouts bound job duration. A separately approved, time-limited risk acceptance could be technically defensible for isolated non-release work on a trusted checkout, scoped to the reviewed lockfile and advisory. The residual tooling risk would remain through expiry or until a verified fix. Production release and the enforcement gate should remain blocked. No exception is in effect.

## 11. Recommended decision

Keep P7-08 blocked. When a fix is published, confirm the advisory and registry agree on fixed versions for both lines. If only one line receives a patch, wait for a supported consumer upgrade that removes or replaces the affected line. Choose the smallest compatible update for Expo SDK 58, Metro, Jest and Vite, then update the lockfile and run `npm ci`, the dependency tree and both audit modes, the exact CI gate, Expo compatibility, web and mobile lint, typecheck and tests, and Android/iOS exports. Confirm the hosted security gate and the independent re-audit before advancing.

**P7-08: BLOCKED**

**DEPENDENCY SECURITY GATE: FAIL**

**PHASE 8 READINESS: NOT READY**
