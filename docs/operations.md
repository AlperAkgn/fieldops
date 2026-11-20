# FieldOps operations and recovery

## Deployment configuration checklist

- Run staging and production with their own `ASPNETCORE_ENVIRONMENT`, database, object bucket, signing key, data-protection key ring, hostnames, allowed browser origins, and provider credentials. The repository Compose configuration is for local Development.
- Set `AllowedHosts` to exact API hostnames separated by semicolons. Configure `Cors__AllowedOrigins__0` and further numbered values as exact HTTPS origins for the admin web. Preserve the API host, HTTPS scheme, WebSocket upgrade, and origin at ingress.
- Terminate trusted TLS at Kestrel or a managed ingress. For ingress termination, configure `Proxy__KnownProxies__0` and further exact direct peer addresses. One proxy hop is trusted. Configure the trusted peer to overwrite inbound forwarding headers and restrict the backend network to that peer. Never log the WebSocket access-token query parameter or cookies at ingress. [Forwarded headers in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)
- Set `ConnectionStrings__Database` with `SSL Mode=VerifyFull`, the DNS name matching the server certificate, a trusted CA, and a restricted runtime database role. Use separate migration credentials with DDL grants and apply migrations before the API becomes ready. Confirm query and migration timeouts against staging data.
- Inject `Jwt__SigningKey`, SMTP credentials, storage keys and any Expo provider credentials through the deployment secret store. Keep the data-protection key ring on persistent shared storage with restricted access, protected backups and platform-managed encryption at rest. Rotate provider credentials in coordination with replica rollout. Changing the current single JWT signing key invalidates current access tokens, and losing the data-protection keys invalidates protected cursors and unread protected data.
- Configure TLS-capable SMTP with `Email__UseStartTls=true`. Configure object storage with a TLS endpoint, a private provisioned bucket, and a least-privilege credential scoped to that bucket. Keep `ObjectStorage__CreateLocalBucket=false` and verify anonymous object access is denied. Apply consistent object lifecycle and backup policy. Public enablement of the MinIO console, Mailpit, OpenAPI, and telemetry is unnecessary for the production API.
- Keep the API non-root with `no-new-privileges`. Size CPU, memory, request concurrency and temporary storage on staging using realistic uploads and expected tenant volume. Allow graceful termination and keep upload temporary storage writable. Restrict PostgreSQL and object storage to the private network and keep probes at the ingress or orchestration layer. Use one API replica for the current in-process SignalR connection registry. Multi-replica delivery requires shared fan-out before expansion.

## Signals and alert triage

Logs are JSON on stdout with UTC timestamps. The HTTP request record includes severity, service and environment, server request ID, W3C trace and correlation ID, operation, route template, duration, outcome and authenticated tenant context when available. `X-Correlation-ID` also appears on API responses. Keep logs access-controlled. Query strings, headers, tokens, SQL parameters, and business payloads are excluded from FieldOps telemetry. Preserve redaction in infrastructure logs and prevent collectors from attaching sensitive request data.

Configure `Telemetry__OtlpEndpoint` as the HTTPS origin of an approved private OTLP/HTTP protobuf receiver. Loopback HTTP is supported for local diagnostics. The service sends `/v1/traces` and `/v1/metrics` when configured and has no metric scrape route. Use a local TLS receiver or the integration test to validate export before using an external receiver. Export is optional and asynchronous. Outbox dispatch spans link to the originating write via the persisted W3C trace context. Propagate trace context according to the receiving system's trust boundary. [OpenTelemetry OTLP protocol](https://opentelemetry.io/docs/specs/otlp/)

| Signal | Triage |
| --- | --- |
| `fieldops.operations`, `fieldops.duration` | Filter by bounded `operation` and `outcome`. Use `operation=http` for ordinary HTTP, including rejected upgrades. Use `websocket.upgrade` for time to successful HTTP 101 or HTTP/2 CONNECT response headers and `websocket.connection` for the lifetime after that boundary. Auth, work-order writes, storage and sync add detail series. |
| `postgresql.command` | Correlate command latency and failures with database capacity, locks and readiness. Inspect approved SQL plans in staging with parameter logging disabled. |
| `fieldops.signalr.connections` | Review reconnect trend and compare fan-out with HTTP reads. A socket may disconnect between commit and delivery, so clients revalidate on reconnect. |
| `fieldops.outbox.pending`, `fieldops.push.pending` | Compare backlog with `outbox.dispatch` retry/dead-letter outcomes, push send/receipt status and `collaboration.worker` failures. Rows may be pending during backoff. Gauges refresh from PostgreSQL every 30 seconds when the worker is healthy. Use `max` for shared backlog gauges monitored from multiple workers. |
| `email.worker`, `storage.put`, `storage.get`, `storage.download`, `storage.cleanup`, `sync.command` | `storage.get` describes the provider fetch to headers. `storage.download` includes transfer and records timeout/cancellation. Triage provider errors independently and track sync conflicts separately from network errors. |

`/health/live` confirms the process can answer. `/health/ready` uses a separate unpooled PostgreSQL connection and verifies the migration model. The connection and command timers are at most two seconds each, with a 3.5-second shared database budget. The four-second registration timeout and 4.5-second deadline armed before routing reserve response time within the five-second HTTP contract. An expired request can return 503 while dependency cancellation unwinds. Keep probes separate and restrict their ingress. Push, SMTP and storage use feature-specific failure signals. Confirm alert thresholds and paging ownership in staging. [Npgsql timeouts](https://www.npgsql.org/doc/connection-string-parameters.html#timeouts-and-keepalive)

## Dependency gate

After `npm ci`, verify the full tree with `npm audit --audit-level=high` and inspect the production graph with `npm audit --omit=dev --json`. The current picomatch High advisory fails the full gate. Keep glob inputs confined to trusted repository and build configuration. Production bundle inspection characterizes exposure, and a verified upstream update is still required to close the advisory. [Current dependency evidence](phase-7-final-quality-gate-remediation.md#4-p7-08-dependency-inventory)

## Incident recovery

1. Identify the affected service and time window using health checks and telemetry. Capture the correlation ID and deployment revision. Restrict packet captures, dumps, object keys and provider payloads to incident responders.
2. For PostgreSQL loss, check connectivity, TLS, pool pressure and migration state. Keep traffic off unready replicas. Restore the dependency, wait for readiness, and retry through existing idempotency or version checks. Inspect contested aggregates and audit before attempting manual changes.
3. For pending collaboration events, inspect `CreatedAt`, `Attempts`, `NextAttemptAt` and `FailedAt`. The worker checks after a 500 ms idle delay and yields for 50 ms when it consumed a full 25-event batch. Housekeeping has its own three-second cadence. Eight failures stop automatic delivery. Retry starts at the recorded attempt transition, and event identity stays stable. Transport delivery is at least once, and delayed or duplicate invalidations should cause fresh authorized reads. Use an approved recovery operation after checking current authorization and business relevance before replaying a dead letter.
4. For object storage loss, restore connectivity and bucket access. A failed upload stays non-downloadable. Retrying the same bytes and idempotency key resumes the reservation while it exists. Download uses an eight-second deadline through provider and client transfer. A timeout before headers returns safe HTTP 503. After a response starts, the API terminates the partial transport and closes the provider stream. Discard the incomplete file and start a new authorized GET after recovery. The cleanup worker handles stale unready reservations. Signature, size and checksum checks provide file validation. Malware scanning is a separate release decision, especially for third-party PDFs.
5. For push interruption, review ticket/receipt status and active installation/session bindings. Reconnect and authorization rechecks guard SignalR independently. Do not resend protected payloads or manually transfer tokens between users or workspaces.

## Backups and restore

Take encrypted PostgreSQL backups with verified retention and, where required, WAL archives for the agreed RPO. Back up the private object store and the data-protection key ring with the database recovery point. Retain the matching schema/image and approved secret versions. Define retention for audit, logs, protected data and completed/dead delivery rows with operations and legal owners. No automatic destructive retention is configured here. Rehearse the restore with separate credentials and private destinations. Check row counts, migration state, key availability, attachment access and item checksums before service cutover.

For a disposable local restore drill, run `pg_dump -Fc` against the isolated Phase 7 database. Restore with `pg_restore --exit-on-error` into a newly created named database inside that same PostgreSQL container. Compare migration history, work orders, audit and outbox rows with the source. The drill exercises PostgreSQL logical backup. Object restore, key-ring restore and a production recovery point require separate validation.

If `Phase7OutboxReliability.Down` reports persisted delivery state, leave the current schema and workers in their supported configuration while evaluating a forward fix or a coordinated restore. Preserve a backup of the current database and delivery state. For a restore, quiesce writes and workers, rehearse on a new database, and reconcile changes since the restore point, including terminal event IDs and potentially sent notifications, before reopening delivery. Pair the restored schema with its compatible release, object state and protected keys. Avoid clearing retry columns or migration-history rows to force a downgrade.

## Isolated benchmark and storage drill

Run from the repository root after locked dependency installation and the local HTTPS setup in [Deployment](deployment.md). Use the ordinary development `.env` only for local credentials, with distinct Compose volumes under project name `fieldops-phase7`.

```sh
export POSTGRES_PORT=5576 MAILPIT_SMTP_PORT=1066 MAILPIT_WEB_PORT=8066
export API_PORT=5096 API_HTTPS_PORT=5466 MINIO_API_PORT=9070 MINIO_CONSOLE_PORT=9071
docker compose -p fieldops-phase7 -f docker-compose.yml -f infrastructure/docker/compose.e2e.yml up -d --build --wait
export NODE_EXTRA_CA_CERTS="$PWD/.certs/fieldops.crt"
export FIELDOPS_PHASE7_BENCHMARK=isolated
node infrastructure/scripts/phase7-benchmark.mjs
node infrastructure/scripts/phase7-benchmark.mjs --dispatch-load
node infrastructure/scripts/phase7-benchmark.mjs --storage-recovery
node infrastructure/scripts/phase7-benchmark.mjs --storage-recovery --restart-api
```

For PowerShell, set the same variables as `$env:NAME = 'value'`. The benchmark creates synthetic accounts and data and emits JSON. It sends traffic only to loopback ports 5466 and 8066. The storage drill pauses the expected isolated MinIO container and resumes it in `finally`. The optional restart flag also checks the synthetic PostgreSQL reservation, then gracefully restarts the isolated API during the upload. If the drill process is forcibly stopped, check MinIO and resume it with `docker unpause fieldops-phase7-minio-1`. Keep image resources, fixture shape and warm-up comparable. Capacity and SLO validation require a larger, representative workload.

For the slow-stream drill, add the proxy on the isolated Compose network without publishing a port. Point only the test API at it using a local override with `services.api.environment.ObjectStorage__Endpoint` set to `http://fieldops-phase7-stream-proxy:9082`.

```sh
docker create --name fieldops-phase7-stream-proxy --network fieldops-phase7_default \
  node:24.21.0-bookworm-slim sh -c 'printf normal > /tmp/fieldops-stream-mode && node /tmp/proxy.mjs'
docker cp infrastructure/scripts/phase7-stream-proxy.mjs fieldops-phase7-stream-proxy:/tmp/proxy.mjs
docker start fieldops-phase7-stream-proxy
# Recreate the isolated API with the extra override, keeping the ordinary Compose files.
node infrastructure/scripts/phase7-benchmark.mjs --slow-download
```

Recreate the isolated API with its ordinary Compose files after the drill. The proxy forwards signed requests to the local MinIO service. It delays either provider headers or the body after eight bytes, then checks that provider and client streams close. The drill creates only synthetic attachments. [Remediation measurements](phase-7-quality-gate-remediation.md)
