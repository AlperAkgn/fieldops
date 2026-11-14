# Technician offline synchronization

Phase 5 implements protocol 1. Native-device acceptance remains conditional. The local workflow requires a new Android/iOS build with Expo SQLite and SQLCipher. Create an authorized download by signing in, selecting a Technician workspace over HTTPS, and checking the last download in **Sync**.

| Available offline | Requires the API |
| --- | --- |
| Downloaded assigned work orders, description, schedule/status, checklist, and up to 10 recent technician-visible notes per order | Initial login, workspace changes, new assignments, detailed contacts/maps, history and additional note pages |
| Checklist item updates, new technician-visible service notes, start, pause, resume and completion | Creating, scheduling, assigning, reopening or canceling work orders, checklist-template administration and internal notes |

The client retains the time each offline action was captured. The server records this separately in the successful receipt and continues to use server time for execution state, audit and business rules. The state machine, required checklist checks and current assignment are revalidated on application. Photo and attachment queues are deferred.

## Local storage and recovery

Expo SQLite stores an authoritative minimized order projection and a typed outbox. An offline change becomes visible through the outbox rows committed in the same local transaction. Dependent edits use their predecessor's predicted next version. Each command has a stable UUID, target, expected version, allowlisted payload, client capture time, sequence, state, attempts and next retry time. The client queues at most 200 edits. An interrupted `sending` row reopens as retryable with the same UUID. An ambiguous Phase 4 direct online write is never converted into a new offline operation automatically.

Each pull page advances its cursor in the same SQLite transaction as its order upserts. Acknowledged rows stay until the cached server version incorporates them, including when the server version arrived before a replay acknowledgment. The local process serializes storage transactions. A key and cache are bound to API origin, tenant, user, role and session. Access/refresh tokens stay outside SQLite. [Offline security](security.md#phase-5-offline-storage)

## Push and pull

`POST /api/v1/sync/push` takes `{ "protocol": 1, "operations": [...] }`, with 1–20 operations and the API's existing 16 KiB body limit. The client normally sends one at a time. It returns HTTP 200 with ordered per-operation results `applied`, `conflict`, `rejected` or `blocked`. `version` is the committed or currently authorized version when available. `duplicate` distinguishes a replay of an already committed operation. A stale expected version maps the normal HTTP 409 domain condition to `work_order_version_conflict` in the batch result. A reused UUID with a different canonical payload gives `sync_operation_reused`. A failed operation blocks later commands for its work order within the batch. The next independent work order can proceed. Authenticated Technician membership and current assignment are required before receipt disclosure.

PostgreSQL commits each accepted operation's receipt, aggregate/version, audit, history and feed change in the same transaction. Receipts are unique by tenant, user and operation UUID, allowing safe replay across device restarts and access-token refreshes. They store a SHA-256 command fingerprint, work-order ID, resulting version, client capture time and server commit-side time. A response lost after commit can be retried safely. The same request after access loss cannot reveal the receipt or version.

`GET /api/v1/sync/pull?protocol=1&cursor=…` returns at most 20 orders plus `cursor`, `hasMore` and `serverTime`. Start without a cursor. Pull uses a repeatable-read snapshot and validates the opaque cursor against the current tenant/user/session and active assignment identities. PostgreSQL serializes watermark updates by tenant in the same transaction as order updates. A second writer cannot publish a higher committed position past an uncommitted lower position. The feed retains the latest position for each work order. The existing API has no hard-delete work-order command.

An expired or authorization-stale cursor returns HTTP 409 `sync_resnapshot_required`. The client hides the prior projection during an authorized resnapshot, removes cached orders and drops queued edits for orders absent from the finished download. Changes to unrelated assignments do not discard still-authorized drafts. A 401 or sync-scope 403 clears the protected local session and cache. This uses authorization invalidation for removal instead of exposing order identifiers outside the technician's scope.

## Sync and conflict recovery

Network changes, foregrounding, a foreground timer and **Synchronize now** initiate a bounded drain. The app checks API reachability before using a rotating refresh proof when offline or near token expiry. Bounded exponential backoff with jitter handles interruptions and retryable failures. Server rate-limit wait applies to the whole outbox and survives restart. The sync center shows last download, retry state, rejection and conflict detail. A rejected or conflicted change holds the same order's dependent tail. A technician can review server state and explicitly apply a compatible intent to the current version with a new UUID, or discard the selected edit and its dependent tail. No automatic merge changes the user's intent.

## Windows and retention

- New local work requires an authorized download from the last **24 hours** and an unexpired protected refresh proof. Clock skew beyond five minutes prevents a lease renewal. Revocation cannot reach a disconnected device before it reconnects or loses this local access window.
- The server accepts previously unseen commands created up to **7 days** earlier or **5 minutes** ahead. An unusable **7-day** cursor forces a full authorized download. An existing identical receipt remains safe to return to an authorized caller.
- Receipts are retained without automatic pruning. Feed storage is one latest row per work order plus one watermark per tenant. Any future deletion, pruning or retention migration must preserve replay and removal behavior. Migration downgrade is blocked when receipts exist.

The exact tests and native acceptance limits are recorded in [the Phase 5 report](phase-5-results.md).
