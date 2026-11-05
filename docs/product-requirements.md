# Product requirements

## Product and delivery boundary

FieldOps is a multi-tenant B2B field service management platform for organizations that dispatch technicians to customer sites. It supports different service sectors without embedding HVAC-specific rules. The target product consists of an ASP.NET Core API, PostgreSQL, a React administration dashboard, and an Expo technician application.

Value proposition: Manage field operations, empower technicians, and keep every service job under control.

The repository starts with **Phase 0: Foundation & Architecture**. Health connectivity, project foundations, migration tooling, local infrastructure, and CI are the only initial executable scope. Product features below are requirements for subsequent phases, not claims of delivered functionality. The latest explicit user instruction overrides the master prompt's automatic continuation: stop after each phase and wait for authorization to start the next.

## Users and boundaries

| Role | Intended access |
| --- | --- |
| Organization Owner | Organization settings, invitations, membership/role management, and all operations |
| Manager / Dispatcher | Customers, locations, assets, work orders, assignments, scheduling, and operational reports |
| Technician | Assigned/authorized work, service notes, checklist updates, status actions, time records, and attachments |
| Viewer | Read-only operational data explicitly allowed by organizational policy |

A user can belong to multiple organizations. Membership role and status belong to the organization; a role in one organization grants no access to another. Internal manager notes are excluded from technician-visible contracts. Tenant isolation and resource authorization are backend responsibilities.

## Functional requirements and phase ownership

| Area | Required behavior | First implementation |
| --- | --- | --- |
| Identity | Registration, login/logout, password reset, current user, short-lived JWT access, hashed rotating refresh tokens, reuse detection, session revocation/deactivation | Phase 1 |
| Organizations | First-owner onboarding, organization selection/creation/profile, membership, invitation/acceptance, role changes, deactivation | Phase 1 |
| Customers | Create/read/update/archive, type, contacts, notes, active status, search/filter/sort/pagination | Phase 2 API; Phase 3 web |
| Service locations | Multiple locations per customer, address/contact information, optional coordinates, authorized map linking | Phase 2 API; Phases 3–4 UI |
| Assets | Customer/location ownership, code/category/brand/model/serial number, installation/warranty dates, status/notes, service history | Phase 2 API; Phase 3 web |
| Work orders | Tenant-unique number, customer/location, optional asset, title/description, priority/status, planned/actual times, creator, audit timestamps, version | Phase 2 |
| Assignments | Multiple technicians, one primary, assigning actor/time, availability checks, justified manager override | Phase 2; Phase 6 calendar |
| Comments | Author/time and internal versus technician-visible content; backend visibility enforcement | Phase 2 |
| Checklists | Template/items, immutable work-order snapshot, required-item completion validation, progress | Phase 2 |
| Mobile work | Secure login, own schedule/tasks, task detail, status actions, notes/checklists, loading/error states | Phase 4 |
| Offline work | Previously authorized cached tasks, queued status/checklist/note/time actions, outbox, incremental synchronization, visible conflicts/retry | Phase 5 |
| Realtime | Authenticated SignalR events after commit, tenant/resource audience checks, reconnect and API revalidation | Phase 6 |
| Files | Camera/gallery, metadata, size/content checks, private S3-compatible storage, authorized download, upload retry | Phase 6 |
| Scheduling | Daily/weekly views, technician filter, reassignment/history, overlap detection; drag/drop follows a working scheduler | Phase 6 |
| Notifications | In-app and Expo push, assignment/reschedule/cancel/update/critical events, user-consented permissions, device lifecycle/retry | Phase 6 |
| Reporting | Real totals, active/completed/overdue jobs, active technicians, status/priority distributions, completion rates, workload, duration, date range | Phases 3 and 6 |
| Quality/release | Progressive tests from each phase; expanded security/performance/accessibility/observability in Phase 7; demo/docs/release preparation in Phase 8 | All phases; 7–8 expand |

## Work order invariants

Priorities are `Low`, `Medium`, `High`, and `Critical`. Allowed normal transitions are exactly:

| Current status | Allowed next statuses |
| --- | --- |
| Draft | Scheduled, Cancelled |
| Scheduled | Assigned, Cancelled |
| Assigned | InProgress, Cancelled |
| InProgress | OnHold, Completed |
| OnHold | InProgress, Cancelled |
| Completed | None |
| Cancelled | None |

`InProgress → Cancelled` is deliberately absent. A future reopen operation requires a separate authorized command, a reason, and audit evidence; ordinary editing cannot reopen terminal records. Scheduling requires a valid positive interval. Assignment requires active technician membership and preserves tenant/customer/location/asset consistency. Completion requires all required checklist snapshot items and valid current version; actual times are server-controlled and consistent. Significant transitions and assignment overrides record actor, time, reason when required, and before/after state in the same transaction.

## Experience requirements

The web product will offer login, overview, customers, assets, work orders/detail, scheduling, team, reports, and settings. Navigation/actions must correspond to real authorized workflows. Use responsive layout, keyboard access, focus visibility, consistent spacing, light/dark theme, loading/empty/error feedback, validated forms, confirmations, and pagination.

The technician app will prioritize touch targets, safe areas, readable field information, pull-to-refresh, today's schedule, task filters, map links, checklist/note/status flows, notifications, profile/logout, and a sync center with queued/failed/conflicted operations. It is a native workflow rather than a copy of the web dashboard. UI copy starts in English through message dictionaries so Turkish can be added later.

## Nonfunctional requirements

- Use strict TypeScript, nullable C#, explicit validation, asynchronous I/O, cancellation, DTO contracts, and controlled transactions.
- Store instants in UTC; display in the organization's IANA time zone. Explicitly handle ambiguous/nonexistent local times and half-open scheduling intervals.
- Enforce tenant-scoped reads/writes, resource permissions, safe errors, secrets isolation, HTTPS outside local development, upload limits, and rate limits.
- Persist records in PostgreSQL with foreign keys, deliberate deletion rules, concurrency tokens, tenant indexes, migrations, and tested transaction behavior.
- Keep audit history separate from diagnostic logs. Avoid tokens, passwords, personal note contents, and signed URLs in logs.
- Provide structured logging and live/ready health in the foundation; expand OpenTelemetry tracing/metrics in Phase 7.
- Run supported lint/format, typecheck, build, and relevant tests at every phase boundary. Do not label unexecuted checks as successful.
- Use lockfiles and officially compatible dependencies; add feature libraries at their first implementation phase.

## Out of initial product scope

AI assistants/scheduling, route optimization, billing/accounting/payments, blockchain, microservices, Kubernetes, and Redis without a demonstrated requirement are excluded. Offline administration, reassignment, and initial login are unsupported. Offline attachment queuing is deferred beyond the initial sync release.

## Acceptance and traceability

The per-phase acceptance gates and test scenarios are in [development-roadmap.md](development-roadmap.md). Current results and limitations are recorded in [implementation-status.md](implementation-status.md). Proposed data contracts, security rules, and sync behavior are developed in [database.md](database.md), [api.md](api.md), [security.md](security.md), and [offline-sync.md](offline-sync.md).

Release demonstration data is development/demo-only: one organization, one owner, one manager, three technicians, ten customers, fifteen locations, twenty assets, work orders in varied states, checklists, and history. No real personal data, default production seed, or credentials are introduced by Phase 0.
