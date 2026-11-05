# API contracts

## Phase 0 surface

| Method/path | Purpose | Access |
| --- | --- | --- |
| `GET /health/live` | Process liveness, independent of database availability | Anonymous, minimal operational output |
| `GET /health/ready` | PostgreSQL connectivity and zero pending EF migrations | Anonymous, minimal operational output |
| `GET /openapi/v1.json` | Generated OpenAPI specification | Development environment only |

Health responses use JSON with an aggregate `status`. Liveness returns `{ "status": "Healthy", "checks": [] }`. Readiness returns `{ "status": "Healthy", "checks": [{ "name": "postgresql", "status": "Healthy" }] }` when PostgreSQL is reachable and all migrations are applied. A failed check reports `Unhealthy` and HTTP `503`, without connection strings, exception messages, or stack traces. Probe connections and commands have bounded timeouts; responses include no-store/no-cache headers. Development OpenAPI includes both real GET routes, their typed JSON schemas and readiness 200/503 responses. No identity, tenant, or business endpoints are implemented in Phase 0. Runtime evidence is recorded in [implementation-status.md](implementation-status.md).

## Planned conventions

Business endpoints use `/api/v1`. Contracts are explicit DTOs with UUID identifiers, UTC ISO-8601 instants, stable enum representations, and an integer version for mutable aggregates. Never bind TenantId from a request body. Resolve current tenant from validated membership and token/session context; organization switching issues a context valid for the chosen membership. Endpoint policies and resource checks apply independently of client navigation.

Use resource JSON directly, collection responses shaped as `{ items, page, pageSize, totalCount }`, and safe ProblemDetails for errors. Management list requests support bounded `pageSize` (default 25, maximum 100), allowlisted sort/filter fields, stable ID tie-breakers, and parameterized search. Sync uses an opaque cursor rather than page numbers. These business DTOs are design contracts to implement and test in their owning phases.

| Status | Planned meaning |
| --- | --- |
| 200 / 201 / 204 | Read/update result, created resource with Location, or completed bodyless action |
| 400 | Malformed input, field validation, or unsupported sort/filter |
| 401 | Missing/invalid/expired authentication |
| 403 | Authenticated caller lacks an operation permission |
| 404 | Resource absent or outside the caller's tenant/authorized resource scope |
| 409 | Version mismatch, invalid current-state action, assignment overlap, or operation ID reused with a different payload |
| 429 | Rate limited, with retry guidance |
| 500 / 503 | Safe unexpected error / unavailable dependency |

Choose `400` for ordinary validation and `409` for current-state business conflicts; do not introduce inconsistent `422` handling. ProblemDetails contains standard type/title/status, a stable safe `code`, `traceId`, and field `errors` when relevant. Responses never expose EF/provider exception details. Conflict responses may include the current version only after authorization. Refresh/reset/invitation errors do not disclose account existence.

## Planned endpoint/policy map

Every group below is **not implemented in Phase 0**. Policy names describe intended requirements, not existing policy registrations.

| Group and routes | Minimum policy and resource constraint | Phase |
| --- | --- | --- |
| `POST /auth/register`, `/auth/login`, `/auth/forgot-password`, `/auth/reset-password` | Anonymous, validated and rate limited; non-enumerating responses | 1 |
| `POST /auth/refresh` | Valid rotating session credential, reuse detection, CSRF protection for cookie mode | 1 |
| `POST /auth/logout`, `GET /auth/me` | Authenticated session; revoke/inspect own session only | 1 |
| `GET /organizations`, `POST /organizations` | Authenticated user; list own active memberships; creator becomes owner atomically | 1 |
| `POST /organizations/{id}/select` | Active membership in selected organization; issue validated tenant context | 1 |
| `GET /organizations/{id}`, `/organizations/{id}/members` | Active membership; member-detail visibility limited by permission | 1 |
| `PATCH /organizations/{id}`, membership role/deactivation commands | OrganizationOwner; preserve at least one active owner | 1 |
| `POST /organizations/{id}/invitations` | OrganizationOwner; scoped role grant, expiry, hashed single-use token | 1 |
| `POST /invitations/accept` | Authenticated intended verified email; valid unconsumed token; membership transaction | 1 |
| `GET /customers`, `/customers/{id}` | OperationalRead in active tenant | 2 |
| `POST /customers`, `PATCH /customers/{id}`, `DELETE /customers/{id}` | OperationalWrite (Owner/Manager); DELETE archives | 2 |
| `GET /service-locations`, `/service-locations/{id}`; `POST /service-locations`; `PATCH /service-locations/{id}` | Scoped read or OperationalWrite, valid same-tenant customer | 2 |
| `GET /assets`, `/assets/{id}`, `/assets/{id}/service-history`; `POST /assets`; `PATCH /assets/{id}` | Scoped read or OperationalWrite, valid customer/location | 2 |
| `GET /work-orders`, `/work-orders/{id}`, `/work-orders/{id}/history` | Owner/Manager/Viewer policy; technician query restricted to authorized assignments | 2 |
| `POST /work-orders`, `PATCH /work-orders/{id}` | OperationalWrite; valid references, expected version on mutation | 2 |
| `POST /work-orders/{id}/schedule`, `/assign`, `/cancel` | Owner/Manager; validated transition, overlap/override checks and audit | 2 |
| `POST /work-orders/{id}/start`, `/pause`, `/resume`, `/complete` | AssignedTechnician or authorized manager; state/version/checklist rules | 2 |
| `GET/POST /work-orders/{id}/comments`, `GET/PATCH /work-orders/{id}/checklist` | WorkOrderRead/WorkOrderExecute; internal notes hidden from technicians | 2 |
| Checklist template CRUD | OperationalWrite for mutations; tenant-scoped access | 2 |
| `POST /sync/push`, `GET /sync/pull` | Active technician membership/device scope; authorize each operation/resource | 5 |
| Work order attachment upload/download routes | Work-order resource permission; private object authorization | 6 |
| `GET /dashboard/overview`, `/dashboard/work-order-statistics`, `/dashboard/technician-performance` | OperationalReports (Owner/Manager); tenant-scoped aggregates | 3/6 |
| Notification list/read and device registration commands | Own user/device within validated active organization | 6 |
| Authenticated SignalR hub | Tenant and recipient/resource checks; server-selected groups only | 6 |

Registration creates the user, initial organization, and Owner membership in one transaction. Additional organizations require an authenticated creator. Invitation delivery uses a configurable email adapter; no simulated production email success. Forgot/reset-password follows Identity's token/security primitives.

## Idempotency, realtime, and errors

Sync operations carry OperationId, entity/type, ExpectedVersion, ClientCreatedAt, and payload. Their atomic receipt/result contract and duplicate handling are specified in [offline-sync.md](offline-sync.md). State-changing endpoints use explicit action commands; arbitrary PATCH cannot bypass the domain state machine or alter assignment/tenant/version fields.

SignalR messages are post-commit invalidation hints containing minimal identifiers/version, not authoritative replacements for API reads. Clients reconnect and revalidate authorized data after missed events. Provider delivery failures are retried outside the business transaction.

OpenAPI must evolve with actual implemented routes and policies. Phase 1 tests add authentication/challenge/authorization and safe error contracts; Phase 2 adds end-to-end workflow, validation, tenant/IDOR, and concurrency assertions. See [security.md](security.md) and [development-roadmap.md](development-roadmap.md).
