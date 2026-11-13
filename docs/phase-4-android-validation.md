# Phase 4 Android device validation

The target is a Samsung Galaxy A34. The cloud build, APK installation, phone TLS connection and device checklist are pending. The operator signs in to Expo, links the project, reviews the account quota, starts the build and installs the APK.

## Select the APK profile

Run EAS commands from `apps/mobile`. The profiles use the Expo SDK 58 worker and Node 24.21.0 to match the workspace. The Android application ID is `com.fieldops.mobile`.

| Profile | APK behavior | Use |
| --- | --- | --- |
| `android-validation` | Internal distribution with embedded JavaScript and the EAS `preview` environment | Galaxy A34 acceptance with an HTTPS API. Metro is unnecessary. |
| `development` | Internal distribution with `expo-dev-client` and the EAS `development` environment | Interactive diagnostics with a Metro server when needed. |

The acceptance profile requires a device-reachable HTTPS origin during Android generation. It disables cleartext traffic in the generated main manifest and can trust a validation CA scoped to one API hostname. The EAS archive rules exclude local environment files, certificate and signing material, and generated native directories. EAS generates the Android project from `app.json` and the locked workspace dependencies. [Expo monorepo setup](https://docs.expo.dev/build-reference/build-with-monorepos/) · [Installable APK profiles](https://docs.expo.dev/build-reference/apk/)

## Prepare the API before starting the cloud build

From the repository root, run `npm ci` and choose one of the following API setups. Keep the same `EXPO_PUBLIC_API_BASE_URL` for the preflight and the EAS `preview` environment. Supply an origin with the scheme, hostname and optional port. The app appends `/api/v1` to its requests.

### Existing HTTPS staging environment

Use a reachable FieldOps test deployment with a matching hostname and a TLS chain trusted by Android. Prepare a disposable technician and work orders there.

```powershell
$env:EXPO_PUBLIC_API_BASE_URL = 'https://YOUR-STAGING-HOST'
Remove-Item Env:FIELDOPS_ANDROID_CA_DER_BASE64 -ErrorAction SilentlyContinue
npm run android:preflight --workspace=@fieldops/mobile
```

Replace `YOUR-STAGING-HOST` with the actual hostname. The preflight verifies the certificate chain and hostname and requires healthy `/health/live` and `/health/ready` responses. Open the same `/health/ready` URL in the phone browser to check reachability and system trust. The installed APK must still pass the login check. [Android certificate trust](https://developer.android.com/privacy-and-security/security-config)

### Private LAN HTTPS environment

Use PowerShell 7 and a running Docker engine. Put the Galaxy and PC on a trusted network that permits device-to-device traffic. Set `$lanIp` to the PC's Wi-Fi or Ethernet IPv4 address from `Get-NetIPAddress -AddressFamily IPv4`.

```powershell
$lanIp = 'YOUR-PC-LAN-IPV4'
./infrastructure/scripts/New-AndroidValidationTls.ps1 -LanAddress $lanIp

$compose = @('--env-file', '.env.android-validation', '-p', 'fieldops-android-validation',
  '-f', 'docker-compose.yml', '-f', 'infrastructure/docker/compose.android-validation.yml')
docker compose @compose config --quiet
docker compose @compose up -d --build --wait api

$env:EXPO_PUBLIC_API_BASE_URL = "https://${lanIp}:5451"
$caFile = (Resolve-Path '.certs/android-validation/ca.cer').Path
$env:FIELDOPS_ANDROID_CA_DER_BASE64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($caFile))
npm run android:preflight --workspace=@fieldops/mobile
```

The generator creates a seven-day server certificate with the LAN IP and `localhost` in its SAN. It writes a password-protected server PFX and isolated database and JWT settings locally. It exports the public CA certificate and discards the CA signing key. Keep `.env.android-validation` and `.certs/android-validation/server.pfx` on the PC. The public CA is the only certificate file used by the APK build. Android trusts it inside this validation app for the configured host. [Android per-domain trust configuration](https://developer.android.com/privacy-and-security/security-config#TrustingAdditionalCas)

The separate Compose project applies the existing migrations to its own database. The API publishes HTTPS on the selected LAN interface at port 5451 and on loopback at 5452 for web administration. PostgreSQL and Mailpit stay on loopback. If Windows Firewall blocks the phone, allow TCP 5451 for the phone's LAN IP on the private network. If the Wi-Fi isolates clients, use an approved staging endpoint or a network that permits peer traffic.

For fixture creation with the existing admin web, use a second PowerShell terminal at the repository root. Import the generated public CA into the current Windows user's trust store for the duration of local web administration. The phone app uses the CA bundled in its APK.

```powershell
$webCa = Import-Certificate -FilePath (Resolve-Path '.certs/android-validation/ca.cer').Path `
  -CertStoreLocation Cert:\CurrentUser\Root
$env:FIELDOPS_HTTPS_CERT_PATH = '.certs/android-validation/server.pfx'
$passwordLine = Get-Content .env.android-validation |
  Where-Object { $_.StartsWith('FIELDOPS_HTTPS_CERT_PASSWORD=') }
$env:FIELDOPS_HTTPS_CERT_PASSWORD = $passwordLine.Split('=', 2)[1]
$env:VITE_API_BASE_URL = 'https://localhost:5452'
npm run web:dev
```

Open the admin web at `https://localhost:5173` and use Mailpit at `http://localhost:8041` for synthetic activation and invitation messages. Prepare the fixtures described below. When finished, stop Vite and remove only the certificate imported by this terminal using `Remove-Item -LiteralPath "Cert:\CurrentUser\Root\$($webCa.Thumbprint)"`. Stop the isolated services with `docker compose @compose stop` in the first terminal to retain the test database. Run a fresh preflight before the device test if the IP or certificate has changed. A changed API origin or trust anchor requires a new APK.

## Operator steps in Expo and on the Galaxy

Keep the preflight variables in the same PowerShell terminal. Sign in to your own Expo account, link the intended project and verify the owner and build quota. Use the included quota if available and stop at any paid prompt unless you explicitly choose to authorize the cost. Project linking can add `extra.eas.projectId` to `app.json`. [Expo project setup](https://docs.expo.dev/tutorial/eas/configure-development-build/)

```powershell
Set-Location apps/mobile
npx --yes eas-cli@27.9.0 login
npx --yes eas-cli@27.9.0 init
npx --yes eas-cli@27.9.0 env:set preview --name EXPO_PUBLIC_API_BASE_URL `
  --value $env:EXPO_PUBLIC_API_BASE_URL --visibility plaintext --scope project

# Run this only for the private LAN setup. The value is the public CA certificate.
if ($env:FIELDOPS_ANDROID_CA_DER_BASE64) {
  npx --yes eas-cli@27.9.0 env:set preview --name FIELDOPS_ANDROID_CA_DER_BASE64 `
    --value $env:FIELDOPS_ANDROID_CA_DER_BASE64 --visibility plaintext --scope project
}
```

For public staging, remove any previous `FIELDOPS_ANDROID_CA_DER_BASE64` value from the Expo project's `preview` environment. API origins and public CA certificates may be included in the APK. Keep JWT signing keys, database credentials, passwords, refresh tokens, server PFX files and signing private keys out of the EAS application environment and source archive. Expo manages the Android signing credential through its credential flow. [Expo build environment variables](https://docs.expo.dev/eas/environment-variables/usage/)

Inspect the upload contents before requesting the build. Run from `apps/mobile`.

```powershell
$archive = Join-Path $env:TEMP ('fieldops-eas-review-' + (Get-Date -Format 'yyyyMMddHHmmss'))
npx --yes eas-cli@27.9.0 build:inspect --platform android --profile android-validation `
  --stage archive --output $archive
Get-ChildItem -LiteralPath $archive -Force
```

Verify that the root lockfile and mobile source are present and that local secrets, signing files and generated Android files are absent. Review the working-tree changes included in the archive. Once the archive, account and quota are confirmed, start one build.

```powershell
npx --yes eas-cli@27.9.0 build --platform android --profile android-validation --wait
```

If prompted for Android signing, use the intended project's key. Use the existing signer if an installed `com.fieldops.mobile` should be upgraded in place. A different signer cannot update that install. Decide how to handle the existing installation and its data before changing it.

Open the EAS build details on the Galaxy A34, download the `.apk` and open it with the system installer. If Samsung prompts for permission to install from the browser or file manager, grant it for that source for this installation and remove it afterward. Launch FieldOps from the app drawer. The app contacts the configured HTTPS origin at runtime. A LAN-only origin can be used by the phone while it is on the same network as the PC. Keep the APK link within the intended test audience. [Expo APK installation](https://docs.expo.dev/build-reference/apk/#physical-device) · [Internal build access](https://docs.expo.dev/build/internal-distribution/)

## Galaxy A34 acceptance checklist

Prepare an activated technician invited to a Technician workspace, an owner or manager account for concurrent edits, an assigned order with a required checklist item and mapped location, and an unassigned control order in the same workspace. Use synthetic data. Record the Android version, EAS build ID, application version, APK profile, test date and API environment. Record PASS or FAIL only after observing the behavior on the installed APK. Keep evidence free of credentials and customer data.

| # | Device check and expected result | Status |
| --- | --- | --- |
| 1 | Launch the APK from the app drawer. Sign-in or the restored technician view renders without a crash. | NOT RUN |
| 2 | Try an incorrect password and then sign in with the activated technician account. The error is actionable and the valid account opens the app over HTTPS. | NOT RUN |
| 3 | With a valid online session, force stop FieldOps in Android Settings and reopen it. The session and selected workspace restore. Repeat after logout in check 12. | NOT RUN |
| 4 | Select the Technician workspace in Profile. The app shows that workspace and refreshes its data. Prior workspace data disappears on switching. | NOT RUN |
| 5 | Verify that assigned orders appear in Today or Work orders as appropriate. Search, filter and refresh. The unassigned control order stays absent. | NOT RUN |
| 6 | Open an assigned order and check the customer, location, asset, contact and work history against admin web. Open the unassigned control through `fieldops://work-orders/<id>` and observe denial. | NOT RUN |
| 7 | Change the required checklist item and reopen the order. The checked state and progress agree with admin web. Verify the completion gate with the item unchecked before completing the order. | NOT RUN |
| 8 | Record the displayed version before and after the checklist write. Open a status confirmation on the phone, have the manager edit the same order, then confirm. Verify that the stale write is rejected. Use **Reload current data**, observe the current version and retry explicitly. | NOT RUN |
| 9 | Run Start, Pause, Resume and Complete on the test order. Verify each state and status history against admin web. Add the service note in check 10 before completion. | NOT RUN |
| 10 | Add a uniquely identifiable service note and refresh the order. The note appears once in mobile and admin web. | NOT RUN |
| 11 | Open the service location in maps. Check that the expected address or coordinates appear. Return to the order. | NOT RUN |
| 12 | Sign out online. Reopen the app and attempt the old order deep link. Sign-in is required and the prior workspace data is absent. | NOT RUN |
| 13 | Start with a fresh session. Disable phone connectivity while editing a note, then attempt to save. Observe the error and retained draft. Reconnect, reload and verify the order before explicitly retrying. Confirm that the note is saved once. | NOT RUN |
| 14 | Background the app and return while online. Verify that profile and order data revalidate. Revoke the test session from the web session view while the app is backgrounded, then return and verify that sign-in is required and private data is cleared. | NOT RUN |

Use separate test orders for conflict, network interruption and completion if those checks would interfere. Capture the observed result and a sanitized screenshot or correlation ID for any failure. Phase 5 readiness remains **NOT READY** pending the recorded device results.

## Preparation evidence from 2026-11-12

| Local check | Result |
| --- | --- |
| EAS CLI 27.9.0 profile resolver and working-tree archiver | Both Android profiles resolve to internal APKs. The archive contains the lockfile and workspace source. The ignore rules excluded local credentials, Git metadata and the entire generated `android` directory. |
| Locked dependency install and Expo compatibility | `npm ci` and `npm ls --all` succeeded in the local archive. `expo install --check` accepted the SDK 58 patch versions and `expo-dev-client` 58.0.19. |
| Mobile regression | Lint, strict typecheck and Prettier succeeded. All 55 Vitest and 13 React Native component tests passed. |
| Android project generation from the archive | LAN, public CA and default Expo prebuilds succeeded. The Gradle configuration contains `com.fieldops.mobile` and `arm64-v8a`. SecureStore backup exclusions were retained. Both validation manifests disable cleartext. The LAN manifest restricts the generated public CA to the chosen host. Public CA and default generation omit that resource. Missing API origin was rejected for validation. |
| Android JavaScript export | Hermes export completed and contained the selected HTTPS origin. |
| Metro smoke | `npm run mobile:smoke` successfully fetched the Android manifest and JavaScript bundle from the local development server. |
| Local TLS | The certificate generator ran against a local IPv4 address. The preflight accepted a trusted TLS fixture and rejected HTTP, loopback, an API path, an untrusted CA and HTTP 503 readiness. TLS also rejected a mismatched hostname. The real Kestrel executable loaded the generated PFX and returned HTTP 200 for liveness over verified TLS. It returned 503 for readiness with PostgreSQL deliberately unavailable and 404 for OpenAPI in the validation environment. |
| Isolated Compose configuration | `docker compose config --quiet` succeeded. The Docker engine was unavailable during preparation. Starting the stack and verifying healthy PostgreSQL readiness are pending. |
| Expo account, cloud APK and Galaxy A34 | Pending operator steps. All 14 device checks remain `NOT RUN`. |

Preparation added the EAS profiles and archive rules, a validation-only Expo config plugin, the HTTPS preflight, the LAN certificate generator and a Compose overlay. It also aligned the SDK 58 patch dependencies and the root Expo Router pin. The clean lockfile install reported 31 moderate and one low npm advisory. The Android validation setup uses the existing application session and API transport.
