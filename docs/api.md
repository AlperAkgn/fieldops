# API contracts

## Health and development OpenAPI

| Method/path | Purpose | Access |
| --- | --- | --- |
| `GET /health/live` | Process liveness, independent of database availability | Anonymous, minimal operational output |
| `GET /health/ready` | PostgreSQL connectivity and zero pending EF migrations | Anonymous, minimal operational output |
| `GET /openapi/v1.json` | Generated OpenAPI specification | Development environment only |

Readiness returns 200 when PostgreSQL is reachable and migrations are current. An unavailable dependency returns 503 and a redacted check status. The liveness probe checks the process independently. Development OpenAPI includes health, identity and operational contracts with their bearer requirements.

## Phase 1 routes

The routes below use the `/api/v1` prefix. Access tokens go in the `Authorization: Bearer` header. JSON requests reject unknown fields. Roles are strings with the values `OrganizationOwner`, `Manager`, `Technician`, and `Viewer`.

| Method and route | Contract |
| --- | --- |
| POST `/auth/register` | `email`, `displayName`, `organizationName`, optional `timeZoneId` (default UTC). Returns the same 202 envelope for a new or existing valid email. An eligible request creates the pending account, organization, owner membership and confirmation outbox message together. |
| POST `/auth/login` | `email`, `password`, optional `organizationId`. Returns a native token pair for an activated account. An email containing control characters returns 400. |
| POST `/auth/refresh` | `refreshToken`. Returns a new token pair and consumes the previous refresh credential. |
| GET `/auth/web/csrf` | Returns `token` and the antiforgery cookie over HTTPS. HTTP receives 400. |
| POST `/auth/web/register` | Same 202 contract as native registration. Requires HTTPS, allowed Origin and `X-CSRF-TOKEN`. |
| POST `/auth/web/login`, `/auth/web/refresh` | Require HTTPS, allowed Origin and `X-CSRF-TOKEN`. Login uses the same input rules as native login. Refresh reads the cookie. Both return access/session metadata and set a fresh HttpOnly refresh cookie. |
| POST `/auth/logout`, `/auth/logout-all` | Revoke the current or all owned sessions. Return 204. |
| POST `/auth/web/logout` | Requires bearer, Origin, antiforgery and a refresh cookie from the same session. Revokes the family and expires that cookie. |
| GET `/auth/me`, `/auth/sessions` | Current identity, active membership role, and own active sessions. Session listing returns up to 100 entries. |
| DELETE `/auth/sessions/{id}` | Revoke an owned session by ID. Returns 204. |
| POST `/auth/forgot-password`, `/auth/request-email-confirmation` | `email`. Returns the same 202 envelope for any valid address. Eligible requests enqueue mail. |
| POST `/auth/reset-password` | `userId`, `token`, `newPassword`. Returns 204 and revokes all account sessions on success. |
| POST `/auth/confirm-email` | `userId`, `token`, `password`. Confirms the address and sets the password in one transaction. Returns 204 on success. Sign in afterwards. |
| POST `/auth/deactivate` | Deactivates the authenticated account and revokes its sessions. Last active owners receive 409. |
| GET / POST `/organizations` | List the caller's active memberships or create an organization with the caller as owner. Creation requires confirmed email and accepts `name`, `timeZoneId`. |
| GET / PATCH `/organizations/{id}` | Read the active organization or update `name` and/or `timeZoneId` as an owner. |
| POST `/organizations/{id}/select` | Native membership switch with bearer and a current `{ "refreshToken": "…" }` from the same session. Consumes the credential and replaces the tenant-bound session inside the same family. |
| POST `/auth/web/organizations/{id}/select` | Web membership switch with bearer, Origin, antiforgery and the current refresh cookie from the same session. |
| GET `/organizations/{id}/members` | Owner-only membership listing within the active organization. |
| PATCH `/organizations/{id}/members/{membershipId}/role` | Owner-only `role` change. |
| POST `/organizations/{id}/members/{membershipId}/deactivate` or `/reactivate` | Owner-only activation changes. Deactivation revokes affected sessions. |
| GET / POST `/organizations/{id}/invitations` | Owner-only list and create with `email`, `role`. Creation requires confirmed email. Returns invitation metadata. |
| DELETE `/organizations/{id}/invitations/{invitationId}` | Revoke a pending invitation. |
| POST `/invitations/accept` | Authenticated intended recipient with confirmed email submits `token`. Returns the joined organization. |

Use credentials-enabled requests for the web cookie routes. In local development, use `https://localhost:5173` for the web client and `https://localhost:5443` for the API. Both origins use HTTPS and the same hostname for SameSite=Strict cookies. Obtain an anonymous antiforgery token for registration, login, and refresh. Obtain an antiforgery token with the current bearer header for protected web logout or organization selection. Web access tokens belong in memory. Native refresh credentials are intended for SecureStore when the mobile sign-in flow is implemented.

Serialize refresh and organization selection requests within a client session. Reusing the consumed credential with its matching signed bearer on a selection route revokes the session family and returns 401. A subsequent request with access or refresh credentials from that family also returns 401.

Member and invitation lists return arrays with `offset` (default 0) and `limit` (default 50, maximum 100). Request subsequent pages by increasing the offset by the number of returned entries. A short page marks the end of the result set.

The generated Phase 1 OpenAPI describes the applicable ProblemDetails responses. Throttled requests return 429 and a `Retry-After` delay in seconds.

Audit scheduling and assignment records include read-only `metadata` with `previousVersion`, `entityVersion` and `changes`. Each changed field contains `previous` and `current`. A null change entry means that field did not change. The allowlisted fields are `plannedStartAt`, `plannedEndAt`, `technicianUserIds` and `primaryTechnicianUserId`. Legacy audit rows and other action types can have null metadata.

The existing `scheduled`, `rescheduled`, `assignments_changed` and `availability_overridden` actions retain their names. An override action and its scheduling or assignment action describe the same command and carry the same metadata, version and timestamp. The override row retains its reason. Audit metadata is derived from database snapshots within the business transaction. Request bodies cannot supply it.

An ordinary registration opens an organization. An invitation is accepted from a signed-in, verified account. Email messages supply the values for confirmation, reset, and invitation acceptance. The local inbox is available through Mailpit. [Local setup](deployment.md)

## Operational conventions

Business endpoints use `/api/v1`. Contracts are explicit DTOs with UUID identifiers, UTC ISO-8601 instants, stable enum representations, and an integer version for mutable aggregates. Never bind TenantId from a request body. Resolve current tenant from validated membership and token/session context; organization switching issues a context valid for the chosen membership. Endpoint policies and resource checks apply independently of client navigation.

Operational lists return `{ items, page, pageSize, totalCount }` with one-based pages, a default page size of 25 and a maximum of 100. Sorting uses an allowlist and a stable ID tie-breaker. Writes against existing aggregates take `expectedVersion`; after a successful mutation use the returned version for the next request. A conflict returns 409 and the client reloads the resource before retrying.

| Status | Meaning |
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

## Phase 2 routes

All routes in this section use the `/api/v1` prefix and active-tenant membership. Catalog reads allow Owners, Managers and Viewers. Catalog writes require an Owner or Manager. Technician work-order reads and actions are scoped to current assignments. Work-order detail supplies the service address without granting technicians general catalog access.

| Routes | Contract |
| --- | --- |
| GET / POST `/customers`, GET / PATCH / DELETE `/customers/{id}` | Create, read, filter and archive customers. List by search, customer type and active status. Customer types are `Individual` and `Business`. |
| GET / POST `/service-locations`, GET / PATCH / DELETE `/service-locations/{id}` | Locations belong to one customer. Lists support customer and active-status filters. Latitude and longitude are supplied together within geographic bounds. |
| GET / POST `/asset-categories`, GET / PATCH / DELETE `/asset-categories/{id}` | Tenant-local categories with unique normalized names. |
| GET / POST `/assets`, GET / PATCH / DELETE `/assets/{id}` | Assets belong to a valid customer/location pair and may have a category. Lists support customer, location, category, status and active-status filters. |
| GET `/assets/{id}/service-history` | Paged completed work orders for the asset, including archived assets. |
| GET / POST `/work-orders`, GET / PATCH `/work-orders/{id}` | Create in Draft, list and read, and update editable fields. Work-order filters include customer, location, asset, assigned technician, status, priority and planned range. |
| POST `/work-orders/{id}/schedule`, `/assign`, `/cancel` | Owner/Manager commands. Scheduling uses offset-bearing timestamps. Assignments name active same-tenant technicians and one primary. Overlap requires an explicit reasoned override. |
| POST `/work-orders/{id}/start`, `/pause`, `/resume`, `/complete` | Owner/Manager or an assigned technician. Completion requires all required checklist items. |
| GET `/work-orders/{id}/history` | Paged status transitions with actor, time and version. |
| GET / POST `/work-orders/{id}/comments` | Paged comments and authorized append. Internal comments are visible only to Owners and Managers. |
| GET `/work-orders/{id}/checklist`, POST `/work-orders/{id}/checklist/apply`, PATCH `/work-orders/{id}/checklist/{itemId}` | Read progress, apply a one-time template snapshot as an Owner/Manager, and set completion state as an authorized executor. |
| GET / POST `/checklist-templates`, GET / PATCH / DELETE `/checklist-templates/{id}` | Manage reusable versioned templates. DELETE archives the template. |
| POST `/checklist-templates/{id}/items`, PATCH / DELETE `/checklist-templates/{id}/items/{itemId}` | Add, edit or archive a template item without changing existing work-order snapshots. |
| GET `/audit-logs` | Owner/Manager only. Filter by `resourceType`, `resourceId`, `action`, `actorUserId`, `from` and exclusive `to`. |

DELETE on the catalog archives the row and takes `expectedVersion` in the query. Default lists show active rows. Use `includeArchived=true` or the active-status filter for history. Existing work-order and asset history references remain valid.

Catalog lists accept `sort=name|createdAt|updatedAt` with a leading `-` for descending order. Locations also accept `city`, and assets accept `assetCode`. Work-order lists accept `sortBy=createdAt|updatedAt|title|plannedStartAt` and `descending=true|false`. Search treats wildcard characters as literal input.

Customer/location ownership of assets and locations is fixed when they are created. Work-order references can change while the order is a Draft. Catalog PATCH distinguishes omitted properties from explicit null, allowing optional data to be cleared. Work-order PATCH accepts an empty description to clear it and `clearAsset=true` to remove the optional asset.

Schedule and assignment commands use these request shapes. Replace the IDs and expected version with values from the preceding response.

```json
{
  "expectedVersion": 2,
  "plannedStartAt": "2027-01-10T09:00:00Z",
  "plannedEndAt": "2027-01-10T10:00:00Z"
}
```

```json
{
  "expectedVersion": 3,
  "technicianUserIds": ["11111111-1111-1111-1111-111111111111"],
  "primaryTechnicianUserId": "11111111-1111-1111-1111-111111111111",
  "overrideReason": null
}
```

Valid state transitions are Draft → Scheduled or Cancelled, Scheduled → Assigned or Cancelled, Assigned → InProgress or Cancelled, InProgress → OnHold or Completed, and OnHold → InProgress or Cancelled. Completed and Cancelled are terminal. Schedule and assignment changes can retain the current state while it remains editable. Availability checks use half-open intervals `[start,end)` for technician memberships within the active organization.

## Planned endpoint/policy map

| Group and routes | Minimum policy and resource constraint | Phase |
| --- | --- | --- |
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
