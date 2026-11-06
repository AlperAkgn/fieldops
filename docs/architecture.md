# Architecture

## Status and context

Phase 0 establishes the solution, client shells, persistence, health endpoints, containers, and CI. Phase 1 adds identity, organization membership, invitations, and session enforcement. The next domain slice builds on those tenant boundaries. [Implementation status](implementation-status.md)

## System and dependency direction

```mermaid
flowchart LR
    Web[React admin web] -->|HTTP API| Api[FieldOps.Api]
    Mobile[Expo technician app] -->|HTTP API| Api
    Api --> Application[FieldOps.Application]
    Api --> Infrastructure[FieldOps.Infrastructure]
    Application --> Domain[FieldOps.Domain]
    Infrastructure --> Application
    Infrastructure --> Domain
    Infrastructure --> DB[(PostgreSQL)]
    Infrastructure -. Phase 6 .-> Storage[Private S3 / MinIO]
    Api -. Phase 6 authenticated SignalR .-> Web
    Api -. Phase 6 authenticated SignalR .-> Mobile
```

API is the composition root and the single deployable backend. Infrastructure depends inward to implement application contracts. Domain does not reference EF Core, ASP.NET, Identity, or UI libraries. Application does not depend on Infrastructure. No service bus, distributed transaction, generic repository, or mediator framework is needed for the foundation.

| Project | Responsibility |
| --- | --- |
| `FieldOps.Domain` | Future aggregates, value objects, state transitions, invariants, domain errors/events when useful |
| `FieldOps.Application` | Future use cases, DTOs, validation, actor/tenant/resource requirements, provider interfaces |
| `FieldOps.Infrastructure` | PostgreSQL/EF Core, migrations, provider implementations; Identity/storage/notifications later |
| `FieldOps.Api` | HTTP contracts, composition/configuration, safe errors, logging, health/OpenAPI; authentication/policies/hubs later |
| `apps/admin-web` | React/Vite shell now; administration workflows after authenticated APIs exist |
| `apps/mobile` | Strict TypeScript/Expo Router shell now; secure technician workflows and offline persistence later |

Use feature-oriented folders within these projects as capabilities arrive. Logical modules will be Identity/Organizations, Customers/Locations, Assets, Work Orders/Checklists, Scheduling, and Notifications/Sync. Keep cross-module invariants explicit instead of creating dozens of empty abstractions.

## Persistence and transaction model

Use EF Core Code First with the Npgsql provider and PostgreSQL. Match the EF Core and Npgsql major release; [Npgsql's EF Core 10 release notes](https://www.npgsql.org/efcore/release-notes/10.0.html) confirm the provider line. Lock exact resolved packages and use a local EF CLI tool.

Phase 0's initial migration is an empty application schema baseline that establishes EF migration history. It does not introduce a fake business record or bring Phase 1 entities forward. Migrations run explicitly as a deployment/development operation, rather than being applied by every API replica during startup.

For future commands, one application use case owns the transaction. Commit domain changes, audit history, change feed, and idempotency/outbox records together where relevant. Publish notifications and realtime messages after commit; external provider failure must not undo a completed work order. Read queries project directly to DTOs, scope by tenant/resource, use bounded pagination and stable ordering, and avoid unbounded navigation loading.

## Tenant and authorization design

Shared database and shared schema are the initial operating model. User identity is global; role/status are organizational membership facts. An active tenant is validated against the authenticated user's current membership and bound to the request. Token tenant claims alone do not prove current membership or permissions after deactivation.

Future defense layers are authenticated policies, explicit tenant-scoped queries, EF query filters, write guards that stamp/check TenantId, resource checks, and database composite tenant foreign keys. EF filters are useful defaults but can be bypassed and do not replace the other layers; see [EF Core query filters](https://learn.microsoft.com/en-us/ef/core/querying/filters). Background workers use explicit validated tenant/actor context and do not inherit arbitrary web request state. Storage, sync cursors, notification devices, and SignalR audiences obey the same boundary.

## Client architecture

Use npm workspaces with independent applications and a committed lockfile. Do not force React Native and React DOM to use incompatible versions for superficial monorepo uniformity. Follow Expo's SDK-compatible native package set; see the [Expo compatibility matrix](https://docs.expo.dev/versions/latest/).

TanStack Query owns remote server state. Form libraries and validation arrive with real forms. Expo Router owns mobile navigation; React Router owns web routing when administration routes arrive. Keep UI-only state local; introduce Zustand only for demonstrated shared client state. English message dictionaries make later localization additive. Feature-specific dependencies such as SQLite, SecureStore, notifications, image picker, location, charts, and advanced UI components arrive with their use cases.

Phase 0 screens expose real API health and useful connection/error feedback. They do not pretend that login, work orders, metrics, or sync exist. Mobile API origins must be reachable from the physical device; `localhost` addresses the device itself.

## Runtime, errors, and operations

Separate process liveness from PostgreSQL readiness. Readiness uses bounded database connection/command timeouts and drives the Docker API healthcheck. Health output must not include database addresses, exception details, or secrets; the [ASP.NET Core health checks guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0) explains their separate roles. Development OpenAPI describes the implemented GET probes and their typed 200/503 responses; operational product routes will use `/api/v1`.

Use ProblemDetails for safe errors and structured technical logs with request correlation. Never log request bodies or authentication credentials by default. As tenant features arrive, enrich logs with validated tenant/user identifiers and operation/result/duration. Keep business audit records independent of application logs.

Docker Compose provides local PostgreSQL, API, and MinIO infrastructure. MinIO availability is not an implemented upload feature; [ADR 0002](adr/0002-local-object-storage.md) records its archived-upstream limitation and local source-build decision. CI restores locked dependencies and runs backend build/format/tests plus client lint/typecheck/build/tests. External cloud deployment, push credentials, production email, backups, and release signing require later operational setup. [deployment.md](deployment.md) documents the actual local commands and boundaries.

## Alternatives and evolution

Microservices would add deployments, distributed consistency, and tenant context propagation without a current scaling need; keep the modular monolith. EF already supplies unit-of-work/change-tracking behavior, so avoid a generic repository wrapper. Redis, background-job frameworks, and additional infrastructure require measured or concrete needs. See [ADR 0001](adr/0001-architecture.md) for accepted choices and consequences.
