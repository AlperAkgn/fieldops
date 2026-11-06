# Local development and deployment foundation

## Current scope

The local stack runs the Phase 1 identity API, PostgreSQL, Mailpit, and MinIO. The web and mobile foundation screens run on the host.

## Prerequisites

- A current .NET 10.0.4xx SDK patch, minimum 10.0.401 (see `global.json`), Docker Engine with Compose v2, Node 24.21.0, and npm 11.x. Docker/CI use SDK 10.0.403; the API container uses runtime 10.0.14.
- A running Docker Desktop Linux engine on Windows. Use `docker info` to check it; `docker desktop start` starts the installed desktop engine.
- Git, network access to npm/NuGet/container registries and the pinned MinIO source, and available local ports 5080, 5443, 5432, 9000, 9001, 5173, 1025 and 8025.
- Native Android/iOS builds require the corresponding SDK/toolchain; iOS builds require macOS or a separately configured build service. No paid build service is required for Phase 0 checks.

## Local HTTPS certificate

Copy `.env.example` to `.env` and replace the example passwords. Set `JWT_SIGNING_KEY` to 32 cryptographically random bytes encoded as 64 hex characters. Generate it with `openssl rand -hex 32` or PowerShell 7's `[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))`.

Run these commands from the repository root. Use an independent random password to export the PFX and put the same value in `FIELDOPS_HTTPS_CERT_PASSWORD` in the root `.env`.

```powershell
New-Item -ItemType Directory -Force .certs | Out-Null
$certificatePassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
dotnet dev-certs https -ep .certs/fieldops.pfx -p $certificatePassword
dotnet dev-certs https -ep .certs/fieldops.crt --format PEM
dotnet dev-certs https --trust
```

```sh
mkdir -p .certs
certificate_password="$(openssl rand -hex 24)"
dotnet dev-certs https -ep .certs/fieldops.pfx -p "$certificate_password"
dotnet dev-certs https -ep .certs/fieldops.crt --format PEM
dotnet dev-certs https --trust
```

The PFX contains the private key. Compose mounts only the encrypted PFX read-only, and Vite reads it from the same host path. On native Linux with the default Docker UID mapping, grant the non-root container user read access with `setfacl -m u:1654:r .certs/fieldops.pfx` if needed. The PEM export contains the public certificate for smoke-test trust. Use `localhost` in the browser so the hostname matches the certificate. The `.certs` directory and `.env` are ignored by Git and excluded from Docker build context. [ASP.NET Core Compose HTTPS guidance](https://learn.microsoft.com/en-us/aspnet/core/security/docker-compose-https?view=aspnetcore-10.0)

## Start the local infrastructure

```sh
docker compose up -d --build --wait
docker compose ps -a
curl --fail https://localhost:5443/health/live
curl --fail https://localhost:5443/health/ready
curl --fail http://localhost:9000/minio/health/ready
```

PowerShell users can copy the file with `Copy-Item .env.example .env` and inspect health using `Invoke-RestMethod https://localhost:5443/health/ready`. Keep the root `.env` out of Git. Use simple alphanumeric local database credentials so they can be embedded in the Compose connection string.

Compose starts PostgreSQL, a one-shot `migrate` container, the API, Mailpit, and MinIO. A successful migration container exits with code 0. API startup waits for that job and the local mail service. The API readiness check verifies PostgreSQL and the applied migrations.

PostgreSQL 18 stores data under the versioned directory within `/var/lib/postgresql`; the named volume mounts at that parent location, as required by the [official image](https://hub.docker.com/_/postgres). MinIO is compiled from its final source-only security release; see [ADR 0002](adr/0002-local-object-storage.md). A first MinIO build is slower than a normal image pull.

| Service | Address | Purpose |
| --- | --- | --- |
| API | `http://localhost:5080` | Live/readiness checks and development OpenAPI |
| API HTTPS | `https://localhost:5443` | Web authentication, probes and development OpenAPI |
| PostgreSQL | `localhost:5432` | Development database |
| Mailpit | `http://localhost:8025` and SMTP `localhost:1025` | Local confirmation, recovery, and invitation capture |
| MinIO S3 | `http://localhost:9000` | Local object storage; application integration is Phase 6 |
| MinIO console | `http://localhost:9001` | Local object browser; credentials from `.env` |
| Web (host-run) | `https://localhost:5173` | Foundation status screen |

The API, PostgreSQL, Mailpit, and storage ports bind to loopback by default. The HTTPS development port stays on loopback. For mobile health testing on a trusted development network, set `API_BIND_ADDRESS` to the host's LAN address and recreate the API container. Outside Development, the API requires HTTPS for `/api/v1`.

## Run the web and mobile clients

```sh
npm ci
npm run web:dev
# In another terminal:
npm run mobile:start
```

Vite serves HTTPS with the configured certificate and defaults to API origin `https://localhost:5443`. To override that URL, set `VITE_API_BASE_URL` in `apps/admin-web/.env.local`. Web authentication requires both origins to use HTTPS and the same hostname. Expo reads `apps/mobile/.env.local`. Set `EXPO_PUBLIC_API_BASE_URL` to an address the device can reach for the health checks. For an Android emulator use `http://10.0.2.2:5080`. For a physical phone use the development host LAN address. Keep secrets out of `VITE_*` and `EXPO_PUBLIC_*` variables.

Expo's supported client/development build must match the SDK. [Expo explains version matching](https://docs.expo.dev/troubleshooting/expo-go-version-mismatch/). Device startup and permissions require separate native verification; a successful TypeScript check or bundle export does not establish that a native app was exercised.

## Run the API on the host

Keep PostgreSQL running and stop the Compose API to free ports 5080 and 5443.

```sh
docker compose stop api
dotnet tool restore
dotnet restore backend/FieldOps.slnx --locked-mode
```

Provide `ConnectionStrings__Database` and `Jwt__SigningKey` through environment variables or .NET user secrets. Use the database values from local `.env` with `Host=localhost`. For local mail capture start `docker compose up -d mailpit` and set `Email__Host=localhost`, `Email__Port=1025`, `Email__FromAddress=noreply@fieldops.test`, and `Email__UseStartTls=false`. Then run the migration and API:

```sh
dotnet ef database update --project backend/src/FieldOps.Infrastructure --startup-project backend/src/FieldOps.Api
dotnet run --project backend/src/FieldOps.Api -- --urls 'http://localhost:5080;https://localhost:5443'
```

The host launch profile selects Development. The explicit URLs also start HTTPS using the trusted development certificate. The migration chain creates EF history and the Phase 1 identity, membership, session, and mail outbox tables.

## Email and key persistence

Mailpit captures local SMTP messages at port 8025. Use addresses under `fieldops.test` for local exercises. Confirmation and recovery return 202 when a valid request is accepted. Eligible requests enqueue an encrypted message for the dispatcher. SMTP delivery retries use bounded backoff. The worker removes delivered messages and retains failed messages for up to seven days.

Compose stores the Data Protection key ring in the `api_keys` volume. Keep the database and key ring together when restoring local data. An environment with multiple API instances needs a shared protected key ring and consistent application name. Configure a trusted SMTP host, sender, STARTTLS, and provider credentials through deployment secrets for external delivery.

## Verification

```sh
dotnet build backend/FieldOps.slnx --configuration Release --no-restore
dotnet format backend/FieldOps.slnx --verify-no-changes --no-restore
# Set FIELDOPS_TEST_DATABASE before running the full test suite.
dotnet test backend/FieldOps.slnx --configuration Release --no-build
npm run lint
npm run typecheck
npm test
npm run web:build
npm run mobile:check
npm run mobile:export
npm run mobile:smoke
```

`FIELDOPS_TEST_DATABASE` must point to a disposable local PostgreSQL instance and an administrative database such as `postgres`. The test account needs `CREATE DATABASE` permission. Tests create unique `fieldops_test_*` databases and drop only those databases in cleanup. They never recreate the configured application database. Without a test database, explicitly run `dotnet test backend/FieldOps.slnx --filter "Category!=Database"` and report that the real database tests were excluded; the unfiltered suite fails if database configuration is missing.

`mobile:smoke` starts Expo on a temporary loopback port, verifies Metro status and an Android manifest/HTTP JavaScript bundle, then terminates its own process. It catches live resolver/startup failures that a static export can miss. It does not launch a native app.

On a disposable Compose stack with the default ports, run `python infrastructure/scripts/smoke-web-auth.py --ca .certs/fieldops.crt`. The script checks hostname and certificate trust, uses synthetic accounts, reads local Mailpit and reaches the authentication rate limit. It leaves its synthetic account in that stack.

CI runs locked restores, backend build/format/tests against PostgreSQL, client lint/typechecks/tests, the web build, Expo compatibility, Android/iOS exports, and the live Metro smoke. The infrastructure job builds Compose, reapplies migrations, runs the HTTPS smoke test and cleans up its disposable CI volumes. [Implementation status](implementation-status.md)

## Stop, inspect, and upgrade

`docker compose logs --tail=100 api migrate postgres minio` inspects local services. Logs may contain local operational details; review before sharing. `docker compose down` stops services and keeps database/storage volumes. Removing named volumes deletes local data and is not part of the normal shutdown instructions.

After new migrations are introduced in later phases, first run `docker compose build api` so the image contains the new compiled migrations, then run `docker compose run --rm migrate` as an explicit operation and recreate the API. For a source update the combined workflow is `docker compose up -d --build --wait`. Review image and package pin changes before upgrading; major PostgreSQL upgrades require a database upgrade or dump/restore strategy.

## Before staging or production

Phase 7/8 must deliver separate runtime and migration database roles, verified tenant isolation, TLS/reverse proxy and explicit allowed origins, managed secrets, a maintained object-store decision, backup/restore drills, migration rollout/rollback planning, OpenTelemetry collection, log retention, rate limits, dependency review, and release acceptance. The current development superuser credentials, HTTP listener, and MinIO setup are local-only choices. No production environment or demo credentials exist yet.
