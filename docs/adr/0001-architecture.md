# ADR 0001: Foundation architecture and phased delivery

- Status: Accepted for Phase 0
- Date: 2026-11-05
- Scope: Foundation implementation and direction for later phases

## Context

FieldOps targets multi-tenant field service operations across web, native mobile, and a shared backend. Reliable tenant isolation, auditable workflow, and offline technician updates matter more than infrastructure breadth. The repository starts empty. The user requires all requirements to be analyzed now but authorizes only Phase 0 implementation; each later phase requires an explicit instruction to proceed.

## Decisions

1. **Modular monolith with inward dependencies.** Use ASP.NET Core 10 and four projects: Domain, Application, Infrastructure, API. API composes the system; Domain is framework-independent. Keep modules as logical feature boundaries inside one backend deployment. Do not introduce microservices, generic repositories, a mediator framework, Redis, or a job system without a concrete need.
2. **PostgreSQL and EF Core Code First.** Use the compatible EF Core 10/Npgsql 10 family and lock resolved versions. Establish an empty application-schema baseline migration in Phase 0, allowing real migration/tooling/readiness verification without implementing Phase 1 data structures. Run migrations explicitly rather than inside ordinary API startup.
3. **Shared database/schema tenancy, implemented in Phase 1 onward.** Identity users are global and organization membership carries roles/status. Resolve active tenant from validated membership. Combine explicit scopes, query filters, write guards, resource policies, and composite tenant foreign keys. Query filters alone are insufficient. PostgreSQL row-level security is deferred rather than claimed as present.
4. **Strict independent client foundations.** Use npm workspaces, React/Vite for web, React Native/Expo Router for mobile, strict TypeScript, and a lockfile. Follow Expo's tested React/React Native/native-module matrix instead of forcing arbitrary React deduplication. Use TanStack Query for remote state and keep client-only state local. English message dictionaries permit later Turkish localization. The same pinned `expo-router` version is deliberately declared as a root development dependency so hoisted Expo CLI tooling can resolve its typed-route/startup support through Node's workspace resolution. Mobile still owns runtime navigation; web does not import Router. Keep the two declarations aligned and verify actual Metro startup/HTTP bundling as well as static exports.
5. **Add feature dependencies with features.** Identity/validation/auth packages arrive in Phase 1; forms/components/charts arrive with web workflows; SecureStore with native sessions; SQLite with offline data; notifications/image picker/location/Reanimated with the relevant native flows. The required final stack is preserved without installing unused SDKs or requesting unnecessary permissions in Phase 0.
6. **Versioned REST and safe operations.** Planned product routes use `/api/v1`, explicit DTOs, policies, bounded pagination, UTC instants, safe ProblemDetails, and expected aggregate versions. Phase 0 exposes live/ready health and development OpenAPI only. Readiness checks PostgreSQL access and applied migrations; liveness is independent of external services.
7. **Explicit domain semantics.** Work orders follow the master requirement's exact transition table; InProgress cannot directly cancel, and terminal states cannot reopen via PATCH. Checklist templates become snapshots on work orders. Scheduling intervals are `[start,end)` in UTC, permitting adjacent appointments; future assignment mutations serialize overlap checks and record justified overrides. Local organization times use IANA timezone conversion with explicit DST ambiguity handling.
8. **Offline local outbox with authoritative server.** Phase 5 uses atomic SQLite edit+outbox writes, stable UUID operation receipts, version checking, per-aggregate ordering, and a cursor feed with safe committed ordering. Receipt/domain/audit/feed changes commit together. Conflicts are visible; authorization changes remove stale cached access. Encryption is a native-build requirement to verify, not an assumed SQLite property.
9. **Secure session transports.** Phase 1 uses Identity, short-lived JWT access, hashed rotating refresh families and reuse detection. Web access tokens remain in memory with secure HttpOnly refresh cookies and CSRF protection; native refresh credentials use SecureStore. Roles and active membership remain server-validated. No authentication code is moved into Phase 0.
10. **Local-first infrastructure and honest verification.** Compose provides PostgreSQL/API and local MinIO. CI is defined alongside meaningful foundation tests. Runtime, migration, native-device, and remotely hosted CI results are reported separately. External email/push/cloud credentials and production deployment are later operational work.
11. **MinIO maintenance exception.** The upstream community repository is archived, and the final security release is source-only. Pin the local source build to the reviewed security commit rather than ship a known older binary. Keep it local-only and review a maintained S3-compatible provider before Phase 6. This is a documented development compromise, not a production storage recommendation. [ADR 0002](0002-local-object-storage.md) records the exact source pin and operational consequences.
12. **Phase gates are authoritative.** Stop after Phase 0 verification/reporting and update implementation status. The master prompt's instruction to continue automatically is superseded by the user's explicit phase-at-a-time rule. Apply the same gate to Phases 1–8.

## Consequences

The foundation is small enough to inspect and run while preserving the requested platform choices. Empty Domain/Application layers establish valid dependency boundaries without inventing business abstractions. The initial database is intentionally not the target ERD; future migrations introduce tables alongside their security/tests. Minimal health screens provide useful real connectivity instead of unsupported product claims.

Shared-schema tenancy requires disciplined enforcement and adversarial real-database tests in every module. An offline device cannot immediately learn about revoked permissions, so supported offline duration, cache minimization/encryption, scope changes, and retention become explicit Phase 5 acceptance gates. Native builds, signing, and push need tools/credentials that web builds do not verify. MinIO's archival adds maintenance risk and must be revisited before storage features or production deployment.

## Alternatives considered

| Alternative | Decision rationale |
| --- | --- |
| Microservices and event broker now | Deployment/distributed consistency cost exceeds current organizational/scaling need |
| Database/schema per tenant now | Higher provisioning/migration complexity; retain an escape path if isolation/compliance later requires it |
| Full target schema in Phase 0 | Would implement later-phase ownership and untested constraints prematurely |
| Generic repository/CQRS framework everywhere | Adds indirection around EF and simple use cases without demonstrated value |
| Tokens in browser localStorage | Persistent script-readable refresh credentials are inappropriate for the selected web session design |
| Last-write-wins offline changes | Could lose service work and bypass authoritative workflow/version rules |
| Unencrypted SQLite assumed safe | Fails the stated privacy requirement; encryption capability must be verified in the native binary |
| Latest unpinned dependencies/container tags | Reproducibility and compatibility require reviewed versions and lockfiles/pinned sources |

## References

These official references support the selected technologies; the combined design and phase boundaries are FieldOps engineering decisions.

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [Npgsql EF Core 10 release notes](https://www.npgsql.org/efcore/release-notes/10.0.html)
- [Expo SDK compatibility matrix](https://docs.expo.dev/versions/latest/)
- [Vite toolchain requirements](https://vite.dev/guide/)
- [EF Core global query filters](https://learn.microsoft.com/en-us/ef/core/querying/filters)
- [EF Core optimistic concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [Expo SQLite / SQLCipher](https://docs.expo.dev/versions/latest/sdk/sqlite/#sqlcipher)
- [MinIO upstream repository](https://github.com/minio/minio) and [final release notes](https://github.com/minio/minio/releases/latest)
