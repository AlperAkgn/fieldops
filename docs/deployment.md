# Local development and deployment foundation

## Current scope

Phase 0 supports local Docker services and host-run web/mobile development. It does not deploy to a cloud account, provision staging, configure TLS certificates, implement identity, or constitute a production release.

## Prerequisites

- A current .NET 10.0.4xx SDK patch, minimum 10.0.401 (see `global.json`), Docker Engine with Compose v2, Node 24.21.0, and npm 11.x. Docker/CI use SDK 10.0.403; the API container uses runtime 10.0.14.
- A running Docker Desktop Linux engine on Windows. Use `docker info` to check it; `docker desktop start` starts the installed desktop engine.
- Git, network access to npm/NuGet/container registries and the pinned MinIO source, and available local ports 5080, 5432, 9000, 9001, and 5173.
- Native Android/iOS builds require the corresponding SDK/toolchain; iOS builds require macOS or a separately configured build service. No paid build service is required for Phase 0 checks.

## Start the local infrastructure

From the repository root, copy `.env.example` to `.env`, replace the example passwords, then run:

```sh
docker compose up -d --build --wait
docker compose ps -a
curl --fail http://localhost:5080/health/live
curl --fail http://localhost:5080/health/ready
curl --fail http://localhost:9000/minio/health/ready
```

PowerShell users can copy the file with `Copy-Item .env.example .env` and inspect health using `Invoke-RestMethod http://localhost:5080/health/ready`. The root `.env` configures Compose only. Keep it out of Git. Use simple alphanumeric local database credentials; connection-string special characters require correct Npgsql quoting.

Compose starts PostgreSQL, a one-shot `migrate` container, the API, and MinIO. A successful `migrate` container exits with code 0; this is expected. API startup waits for migration success. The API image has a readiness healthcheck, so `--wait` checks actual PostgreSQL/migration readiness instead of only process startup. The normal API process never applies migrations automatically. The HTTP probes are also useful for diagnosing each service independently.

PostgreSQL 18 stores data under the versioned directory within `/var/lib/postgresql`; the named volume mounts at that parent location, as required by the [official image](https://hub.docker.com/_/postgres). MinIO is compiled from its final source-only security release; see [ADR 0002](adr/0002-local-object-storage.md). A first MinIO build is slower than a normal image pull.

| Service | Address | Purpose |
| --- | --- | --- |
| API | `http://localhost:5080` | Live/readiness checks and development OpenAPI |
| PostgreSQL | `localhost:5432` | Development database |
| MinIO S3 | `http://localhost:9000` | Local object storage; application integration is Phase 6 |
| MinIO console | `http://localhost:9001` | Local object browser; credentials from `.env` |
| Web (host-run) | `http://localhost:5173` | Foundation status screen |

Database and storage ports bind to loopback. The development API binds to host interfaces so a physical phone can reach it; use a trusted development network. HTTPS and restricted deployment ingress are required before shared environments are introduced.

## Run the web and mobile clients

```sh
npm ci
npm run web:dev
# In another terminal:
npm run mobile:start
```

Vite reads `apps/admin-web/.env.local`. Set `VITE_API_BASE_URL=http://localhost:5080` if overriding the default. Expo reads `apps/mobile/.env.local`; set `EXPO_PUBLIC_API_BASE_URL` to an API address reachable from the device. `localhost` on a phone refers to the phone. For Android emulator use `http://10.0.2.2:5080`; for a physical phone use the development host LAN address. These public URLs are not secrets. Do not store credentials in `VITE_*` or `EXPO_PUBLIC_*` variables.

Expo's supported client/development build must match the SDK. [Expo explains version matching](https://docs.expo.dev/troubleshooting/expo-go-version-mismatch/). Device startup and permissions require separate native verification; a successful TypeScript check or bundle export does not establish that a native app was exercised.

## Run the API on the host

Keep PostgreSQL running and stop the Compose API to free port 5080:

```sh
docker compose stop api
dotnet tool restore
dotnet restore backend/FieldOps.slnx --locked-mode
```

Provide `ConnectionStrings__Database` through an environment variable or .NET user secrets using the values from your local `.env` and `Host=localhost`. The API intentionally fails fast when the connection string is missing. Do not paste credentials into tracked appsettings files. Then:

```sh
dotnet ef database update --project backend/src/FieldOps.Infrastructure --startup-project backend/src/FieldOps.Api
dotnet run --project backend/src/FieldOps.Api
```

The host launch profile selects development mode and port 5080. The current baseline migration creates only EF migration history; all identity, tenancy, and operational tables are deferred to their authorized phases.

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

CI runs locked restores, backend build/format/tests against PostgreSQL, client lint/typechecks/tests, the web build, Expo compatibility, Android/iOS exports, and the live Metro smoke. A separate infrastructure job builds and starts Compose, probes actual health, reapplies migrations, and cleans up its disposable CI volumes. GitHub-hosted execution can only be confirmed after pushing this repository to a configured GitHub remote. Local evidence is tracked in [implementation status](implementation-status.md).

## Stop, inspect, and upgrade

`docker compose logs --tail=100 api migrate postgres minio` inspects local services. Logs may contain local operational details; review before sharing. `docker compose down` stops services and keeps database/storage volumes. Removing named volumes deletes local data and is not part of the normal shutdown instructions.

After new migrations are introduced in later phases, first run `docker compose build api` so the image contains the new compiled migrations, then run `docker compose run --rm migrate` as an explicit operation and recreate the API. For a source update the combined workflow is `docker compose up -d --build --wait`. Review image and package pin changes before upgrading; major PostgreSQL upgrades require a database upgrade or dump/restore strategy.

## Before staging or production

Phase 7/8 must deliver separate runtime and migration database roles, verified tenant isolation, TLS/reverse proxy and explicit allowed origins, managed secrets, a maintained object-store decision, backup/restore drills, migration rollout/rollback planning, OpenTelemetry collection, log retention, rate limits, dependency review, and release acceptance. The current development superuser credentials, HTTP listener, and MinIO setup are local-only choices. No production environment or demo credentials exist yet.
