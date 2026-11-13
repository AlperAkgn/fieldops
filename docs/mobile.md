# Technician mobile application (Phase 4)

The Expo SDK 58 app is an **online-only native technician client**. Today and Work orders show authenticated assignments with server search/status/priority filters and 20-row pagination. Details contain the customer, site/contact, optional asset, assigned team, organization-zone planned/actual times, checklist progress, service notes and paginated status history. Start, pause, resume and complete use the existing API state machine. Completing required items is enforced both by the UI and the server. Maps opens an encoded HTTPS address/coordinate link without requesting device location.

## Run locally

From the repository root, run `npm ci`. Copy `apps/mobile/.env.example` to `apps/mobile/.env.local` and set a **device-reachable** API origin. Public Expo environment values are bundled in the application: never put secrets in them.

```text
EXPO_PUBLIC_API_BASE_URL=https://your-trusted-development-host:5443
EXPO_PUBLIC_ALLOW_INSECURE_HTTP=false
```

The TLS certificate must be trusted by the device and cover that host. The repository's localhost certificate is not automatically valid for a LAN address or Android's `10.0.2.2`. Do not disable certificate validation. A physical device's localhost refers to itself.

For a disposable **debug-only** local emulator flow, use `EXPO_PUBLIC_API_BASE_URL=http://10.0.2.2:5080` and `EXPO_PUBLIC_ALLOW_INSECURE_HTTP=true`. The API must be in Development; access uses the native JSON transport. The opt-in accepts only localhost/private addresses and is ignored in production bundles. Android's generated debug manifest allows Metro/local HTTP; the release manifest does not gain a cleartext exception. Do not use real credentials on a shared development network.

Run `npm run android --workspace=@fieldops/mobile` with the supported JDK, Android SDK/platform tools and a running emulator/attached authorized device. This invokes `expo run:android` and generates ignored native projects from app config. `npm run ios --workspace=@fieldops/mobile` requires macOS/Xcode. `npm run mobile:start` runs Metro for an already installed compatible native app. Changing environment values requires restarting Metro/rebuilding a production bundle.

Activate the account through the invitation/confirmation email and accept the organization invitation using the existing web onboarding. Sign in on mobile, then select the Technician workspace in Profile if the account initially opens its personal owner workspace. Other membership roles receive a workspace-selection screen, not technician operational queries. Password recovery requests use the existing endpoint and direct the user to the browser recovery flow.

## Security and state ownership

- `MobileSession` serializes credential operations and coalesces refreshes. Native authentication uses bearer/JSON, omits cookies and never uses browser authentication endpoints. The access token stays in memory; only an origin-bound, versioned refresh credential/expiry is stored through Expo SecureStore. Passwords and server data are never persisted. No credentials are logged or placed in URLs.
- SecureStore uses a dedicated keychain service and `WHEN_UNLOCKED_THIS_DEVICE_ONLY`. Its config plugin excludes credentials from Android backup. Android uninstall removes the keystore; iOS keychain entries can survive reinstall, so reinstall is not a substitute for sign-out/server revocation. These are platform properties, **not native verification performed in this task**. [Expo SecureStore](https://docs.expo.dev/versions/latest/sdk/securestore/)
- A rotating marker is persisted **before** consuming a one-use refresh proof. After a crash, interrupted response or credential-write failure, the client clears the session and requires sign-in instead of replaying an old token. This deliberately trades seamless recovery for safe reuse detection. Storage failures that prevent both deletion and a signed-out tombstone lock access until cleanup succeeds.
- Logout clears memory access/cache and protected credentials **before waiting for server revocation**, so killing the app during a delayed logout cannot restore the old proof. If offline/expired access prevents confirmation, the signed-out screen says server revocation was unconfirmed; local deletion is not claimed as remote revocation. A previous server session then remains subject to expiry or account session revocation. Queued 401 cleanup rechecks its captured generation so it cannot erase a newer selected workspace/session.
- Cache keys include tenant/user/role/session. Logout/workspace/role changes cancel and clear server state; generation checks discard late responses from a previous scope. Foreground activation revalidates `/auth/me` and invalidates active queries. Server 401 clears access; 403/404 hides inaccessible details. The backend remains the authority for membership and assignment isolation.
- All work-order writers share a QueryClient-local aggregate lock and pending state. Successful responses acknowledge the root/checklist version before refetch. Confirmation dialogs retain the version displayed when opened. Conflicts and ambiguous writes require an explicit reload/retry; no mutation automatically replays or silently adopts another actor's version. Failed notes retain input. Acknowledged server changes are not undone when a later read fails.

The app displays actual start/end times and transition history from server status actions. There is no independent billing/time-entry API, so it does not invent editable time records or label wall-clock duration as billable technician time. English presentation messages are centralized in `src/i18n/en.ts`. Native primitives supply safe areas, keyboard avoidance, 50-point buttons, scalable text, accessibility roles and modal confirmations.

## Automated checks

```sh
npm run mobile:lint
npm run mobile:typecheck
npm run mobile:test
npm run format:check --workspace=@fieldops/mobile
npm run mobile:check
npm run mobile:export
npm run mobile:smoke
```

`mobile:test` runs Vitest session/transport/domain helpers and Jest Expo/React Native Testing Library component tests. Only native boundaries are mocked; screens use the real session and query/write coordinators. The existing health transport tests are retained. The Expo-supported native dependencies and test preset were checked against the SDK package matrix. [Expo unit testing](https://docs.expo.dev/develop/unit-testing/)

The separate integration command **fails** without explicit isolated-stack settings. It creates uniquely named test accounts/orgs through real HTTPS API/SMTP and exercises the actual mobile session/technician client against PostgreSQL, not a mocked HTTP server or in-memory database. Never point it at a shared development or production database.

```powershell
$env:FIELDOPS_MOBILE_TEST_API = 'https://localhost:5450'
$env:FIELDOPS_MOBILE_TEST_MAIL = 'http://localhost:8040'
$env:NODE_EXTRA_CA_CERTS = 'C:/absolute/path/to/local-public-ca.crt'
npm run test:integration --workspace=@fieldops/mobile
```

For a disposable Compose project, override `POSTGRES_PORT`, `API_PORT`, `API_HTTPS_PORT`, `MAILPIT_SMTP_PORT`, `MAILPIT_WEB_PORT` and local certificate path/password; use a distinct `docker compose -p` project with `infrastructure/docker/compose.e2e.yml`. The overlay raises rate limits only for local integration fixtures. There are no new schema migrations. CI runs the integration command inside its existing disposable infrastructure job after starting the same overlay. Hosted CI results require an actual remote run.

## Native acceptance gate

For the Galaxy A34, use the [EAS APK, HTTPS setup and device checklist](phase-4-android-validation.md). It includes a separate LAN validation stack when a trusted staging API is unavailable.

The current Windows host successfully generated the Android project; `expo run:android --no-install --no-bundler` then failed because the Android SDK and `adb` were absent. Java and a device/emulator were also unavailable. No APK compilation, installation, SecureStore-on-device, keyboard/screen-reader or full native flow is claimed. Android/iOS JavaScript exports and Metro smoke are separate checks.

Maestro was evaluated as an Expo-compatible candidate: its quickstart uses an installed Android/iOS app on a running emulator/simulator; its Windows CLI instructions describe Java/WSL2/ADB prerequisites. It was not installed or represented as passing without a native target. [Maestro quickstart](https://docs.maestro.dev/get-started/quickstart) · [CLI prerequisites](https://docs.maestro.dev/maestro-cli/how-to-install-maestro-cli)

Before closing the native gate, verify on a supported device/development build: sign-in and organization selection; own assignments and filter/refresh; denied unassigned deep link; detail/contact/maps; start, required checklist, note, pause/resume, complete; genuine stale-version conflict preserving input; API/network failure and explicit recovery; app background/foreground and process restart/refresh; logout/cache cleanup and revoked session; large text, keyboard and screen-reader navigation. Record the device/OS/build and actual results. Follow the [Expo native environment setup](https://docs.expo.dev/workflow/android-studio-emulator/).

Offline persistence, SQLite/outbox/sync, attachments, camera, push and SignalR are not part of Phase 4. Phase 5 remains separately authorized and must not start automatically.
