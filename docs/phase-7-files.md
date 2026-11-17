# Phase 7 changed files

| Area | Files and purpose |
| --- | --- |
| API startup | `backend/src/FieldOps.Api/Program.cs` applies the proxy, response-header, and structured-logging pipeline. `Security/DeploymentSecurity.cs` validates hosted trust settings. `Diagnostics/ApiTelemetry.cs` adds safe request tracing, metrics and optional OTLP. `Collaboration/WorkOrderHub.cs` tracks authorized socket registrations and fan-out. |
| Telemetry and packages | `backend/src/FieldOps.Application/Common/FieldOpsTelemetry.cs` owns bounded instrumentation. `backend/src/FieldOps.Infrastructure/Persistence/DatabaseTelemetry.cs` observes commands without SQL or parameters. `FieldOps.Api.csproj` and the backend lockfiles record the OpenTelemetry SDK/exporter restore. |
| Collaboration and storage | `backend/src/FieldOps.Infrastructure/Collaboration/CollaborationDispatcher.cs` adds deadlines, retry and dead-letter persistence, and worker metrics. `FieldOpsDbContext.Collaboration.cs` configures outbox fields and the pending index. `AttachmentService.cs` validates the normalized filename. `S3AttachmentStorage.cs` bounds and instruments provider calls. `Email/EmailOutboxWorker.cs` records worker failures. `DependencyInjection.cs` registers the database interceptor. |
| Transaction and queries | `backend/src/FieldOps.Infrastructure/Operations/OperationalWriteContext.cs` records the trace context with the committed outbox event. `WorkOrders/WorkOrderService.cs` reduces detail read round trips. `WorkOrders/SyncService.cs` batches authorized pull collections and counts outcomes. `WorkOrderDetails/WorkOrderDetailService.cs` provides shared checklist summary calculation. |
| Database | `backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261116164921_Phase7OutboxReliability.cs`, its designer, and `FieldOpsDbContextModelSnapshot.cs`. |
| Admin web | `apps/admin-web/src/App.tsx` lazy-loads feature routes. `apps/admin-web/src/components/Layout.tsx` preserves workspace navigation while a route loads. |
| Tests | `backend/tests/FieldOps.IntegrationTests/DeploymentSecurityTests.cs` covers proxy trust, fail-closed hosted config, labels and loopback OTLP. `OutboxRecoveryTests.cs` exercises retries and linked traces on PostgreSQL. `OfflineSyncTests.cs` covers bounded, tenant-authorized note/checklist reads. `CollaborationTests.cs` covers normalized filenames. `ApiFactory.cs`, `ExceptionHandlingTests.cs` and `WorkOrderDetailSecurityTests.cs` keep existing production, safe-error and migration checks aligned. |
| CI and infrastructure | `.github/workflows/ci.yml` pins PostgreSQL and gates high/critical npm advisories. `docker-compose.yml` pins the local PostgreSQL image and hardens API/migration runtime privileges. `infrastructure/scripts/phase7-benchmark.mjs` runs the isolated load and object-storage recovery scenarios. |
| Documentation | `docs/phase-7-results.md`, `docs/phase-7-files.md`, `docs/operations.md`, `docs/implementation-status.md`, `docs/deployment.md`, `docs/security.md`, and `docs/database.md`. |

Run artifacts remain local under `artifacts/phase-7/`. They include pre/post JSON benchmark summaries and PostgreSQL plans, test and build logs, and Playwright reports. The development environment files, certificates, database snapshots and synthetic credentials stay outside version control.

## Quality gate remediation

| Area | Files and purpose |
| --- | --- |
| API boundaries | `FieldOps.Api/Diagnostics/ApiTelemetry.cs`, `FieldOps.Api/Endpoints/CollaborationEndpoints.cs` and `FieldOps.Api/Program.cs` separate socket measurements, enforce the download streaming deadline and bound the readiness response. |
| Persistence and delivery | `FieldOps.Infrastructure/Collaboration/CollaborationDispatcher.cs`, `DependencyInjection.cs`, `Health/PostgreSqlReadinessCheck.cs` and `Persistence/Migrations/20261116164921_Phase7OutboxReliability.cs` update event clocks, dispatcher scheduling, the probe connection and guarded downgrade. |
| Admin web | `apps/admin-web/src/pages/WorkOrdersPage.tsx`, `pages/OrderCollaboration.tsx` and `components/OrderActions.tsx` keep work-order controls active during background reads and retain the aggregate write lock. `apps/admin-web/e2e/admin.spec.ts` stabilizes the loading and frame gates and reproduces the checklist and scheduling control races. |
| Backend tests | `CollaborationTests.cs`, `DeploymentSecurityTests.cs`, `OtlpReceiver.cs`, `HealthEndpointTests.cs`, and `OutboxRecoveryTests.cs` cover telemetry, readiness, real PostgreSQL recovery and migration safety. |
| Local drills | `infrastructure/scripts/phase7-benchmark.mjs` with `phase7-dispatch-load.mjs`, `phase7-stream-drill.mjs` and `phase7-stream-proxy.mjs` reproduces the isolated before/after workload and MinIO stall. |
| Documentation | `phase-7-quality-gate-remediation.md`, `phase-7-results.md`, `phase-7-files.md`, `operations.md`, and `implementation-status.md` record the independent findings, local remediation evidence and release constraints. |

Remediation artifacts are local under `artifacts/phase-7-gate/`. [Quality gate report](phase-7-quality-gate-remediation.md)

## Final quality gate remediation

| Area | Files and purpose |
| --- | --- |
| Readiness entry | `backend/src/FieldOps.Api/Program.cs` and `Health/ReadinessDeadline.cs` count routing and initial request dispatch within the 4.5-second response budget. |
| Backend verification | `HealthEndpointTests.cs` records request, cancellation and fixture timings. `PostgreSqlMigrationTests.cs` verifies separation from an exhausted EF request pool. `OtlpReceiver.cs`, `DeploymentSecurityTests.cs` and `CollaborationTests.cs` verify all received export batches. |
| Workspace alignment | `package.json`, `apps/mobile/package.json` and `package-lock.json` align the six published Expo SDK 58 patches. |
| Evidence | `phase-7-final-quality-gate-remediation.md`, `phase-7-results.md`, `phase-7-files.md`, `operations.md` and `implementation-status.md` carry the final finding and verification status. |

Final verification artifacts are local under `artifacts/phase-7-final/`. [Final quality gate report](phase-7-final-quality-gate-remediation.md)
