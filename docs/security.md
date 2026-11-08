# Security design

## Current boundary

Phase 1 implements the first-party identity and membership boundary. The API checks the session, activated user, organization, and active membership in PostgreSQL before granting protected access. The current membership supplies the effective role. Organization selection validates its two credentials in the switching transaction. [Verification and environment limits](implementation-status.md)

Keep local example secrets in `.env.example`, real values outside version control, and client build variables limited to public configuration. Both `VITE_*` and `EXPO_PUBLIC_*` values are shipped to users. Never place database credentials, signing keys, storage secrets, or email credentials there.

## Identity and session decisions — Phase 1

- Use ASP.NET Core Identity password hashing, reset tokens, lockout/security primitives, and normalized unique user identity. Do not invent password cryptography or store plaintext passwords.
- Registration atomically creates a pending account, initial organization, Owner membership and confirmation outbox entry. Valid new and existing addresses receive the same 202 response without account IDs, credentials or a cookie. The mailbox owner sets the password when confirming. Login, access validation, refresh and organization selection require an active account with confirmed email and a password hash. Login rejects email control characters with 400 before querying PostgreSQL. Duplicate registration leaves the existing account intact. Invitation tokens are random, hashed, single-use, expiring, and bound to the intended verified email and scoped role.
- Issue short-lived JWT access tokens with issuer/audience/signature/expiry validation. Include session and selected organization context; validate current membership/session state so deactivation, role changes, and revocation take effect according to the tested revocation policy.
- Refresh tokens are high-entropy opaque values stored as hashes. Refresh and organization selection serialize on the user row. Selection retires the tenant-bound session and consumes its current credential in the same transaction that creates the successor in the original family. The two selection routes can identify a retired source session from a signature- and lifetime-validated JWT. The switching transaction checks the signed user, session, tenant, security stamp, transport and exact refresh hash before treating a credential as replayed. A valid replay through either route revokes the active sessions in that family and returns 401. A live source session and active source and destination memberships are required to issue new credentials. Clients serialize refresh and selection calls. [RFC 9700 refresh token guidance](https://www.rfc-editor.org/rfc/rfc9700.html#section-4.14.2)
- Web routes put the refresh credential in a Secure, HttpOnly, SameSite=Strict cookie scoped to `/api/v1/auth/web`. They require HTTPS. Mutations also require an allowlisted HTTPS Origin and antiforgery token. Web logout requires the cookie and bearer to identify the same tenant-bound session. Both transports require the current, unexpired refresh credential from the bearer session to select an organization. [Local HTTPS configuration](deployment.md#local-https-certificate)
- Native routes return the refresh credential in JSON and reject browser Origin and fetch metadata headers. A session family is bound to its issuance transport. The mobile sign-in phase will store refresh credentials in SecureStore and keep access tokens in memory. [SecureStore](https://docs.expo.dev/versions/latest/sdk/securestore/)
- Online logout revokes the current session and removes credentials/cache. Offline logout clears local credentials/cache immediately; it cannot promise immediate server-side revocation without network access. Remote/session revocation is a separate online capability, and refresh expiration still limits abandoned sessions.
- Apply per-IP fixed windows before JWT authentication and PostgreSQL session validation. Organization and invitation APIs also use a per-user quota after authorization. Both stages return safe 429 ProblemDetails and `Retry-After`. Configure production HTTPS and forwarded headers only for trusted proxies.

Defaults are 10 minutes for JWT access, seven days for a refresh credential, and 30 days for an absolute session. Organization switching preserves the absolute expiry. Five failed password attempts lock an account for 15 minutes. Default quotas per minute are 30 auth requests per IP, 240 other API requests per IP, and 120 organization or invitation requests per authenticated user. Activation and password reset use Identity's password policy and hasher. Passwords contain 12–128 characters with upper/lowercase, digit, and symbol requirements.

Confirmation and password reset tokens expire after one hour. Invitation credentials expire after 72 hours. Recovery requests for ineligible addresses return the same public response and enqueue no email. Eligible messages enter a transactional outbox with a protected body. SMTP delivery is at least once, with bounded attempts and no token or body content in diagnostic logs.

## Tenant and resource isolation — Phases 1–2

Phase 2 catalog reads require an Owner, Manager or Viewer role, and catalog mutations require an Owner or Manager. Technicians read related customer, address and asset summaries through assigned work orders. Every work-order subresource checks the authorized parent. Only Owners and Managers read or write internal comments and read audit logs. Operational writes revalidate the session and membership after acquiring the organization lock, so a role change committed while a request waited cannot retain stale write authority.

Customer archive keeps relationships and history. It prevents new location or work-order references to the customer. Template updates and archive preserve copied work-order checklist fields. Completing an order checks required snapshot items in the same transaction as the state transition and audit entry. Conflicting versions and assignments return 409. Availability overrides require an authorized manager and a recorded reason.

The active tenant comes from verified authenticated membership. A user-supplied TenantId, URL organization ID, token claim, object key, or SignalR group name is never sufficient authority. Reject inactive organizations/memberships and prevent removing/demoting the last active owner. OrganizationOwner privileges apply only within that organization.

| Layer | Required enforcement |
| --- | --- |
| HTTP/application | Authenticate by default for business routes; named operation policies and resource-level checks |
| Read queries | Explicit tenant scope, assignment/visibility predicates, safe DTO projection |
| EF defaults | Tenant query filter plus separate archive visibility; no ad hoc `IgnoreQueryFilters` in ordinary use cases |
| Writes | Stamp TenantId from trusted context; reject cross-tenant insert/update/delete or TenantId changes |
| Database | Composite tenant foreign keys and tenant-scoped unique constraints |
| Async boundaries | Explicit validated tenant/actor context for workers, sync, storage, devices, and realtime |

Use authorization requirements/handlers for role and resource facts; ASP.NET Core supports named policy requirements as described in [policy-based authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0). Client route guards improve UX and do not enforce data security.

Return 404 for resources outside an authorized tenant/resource scope to avoid disclosing their existence. Internal comments are filtered server-side before serialization, notifications, sync, or logging. Technicians cannot grant themselves assignments or broaden a cached task audience.

## Offline privacy — Phases 4–5

Download the minimum authorized technician dataset and partition local storage by account/organization. Logout and organization/account switches must erase sensitive cache/outbox/attachment files from the previous scope before another scope opens. Make loss of unsynchronized edits clear in the voluntary logout flow, but never retain another user's readable data after logout.

Expo SQLite is not assumed encrypted. For confidential offline production data, require a native development/release build with SQLCipher, a random per-install database key held in SecureStore, and a runtime cipher-availability assertion. SQLCipher is not supported in Expo Go, and a key pragma alone does not prove encryption; see [Expo SQLite's SQLCipher guidance](https://docs.expo.dev/versions/latest/sdk/sqlite/#sqlcipher). Test this on real native builds before claiming at-rest protection. OS backups, keys, temporary files, and device sharing need explicit review. A device remaining offline cannot receive immediate revocation; document bounded offline access/refresh requirements before release.

## Files, realtime, and notifications — Phase 6

Validate upload size, allowed content, detected file signature, and safe image decoding server-side; do not trust extensions or client media types. Use private storage, random tenant-scoped object keys, authorized metadata lookup before download, short-lived access links when needed, and bounded upload/download rates. Never expose storage root credentials to clients or log signed URLs. Define scanning/quarantine policy before enabling broader document formats.

Only the server selects authenticated SignalR groups after membership/resource checks. A tenant group alone is too broad for assignment-limited technician events. Use minimal payloads, re-check authorization on API fetch, and disconnect/revalidate connections when access changes. Device tokens are associated with current user/organization/installation and removed or reassigned on logout. Push text contains no service notes, private addresses, or sensitive customer details. Provider errors retry outside work-order transactions.

MinIO is local development infrastructure only. Its community repository is archived and the final security release is source-only; inspect the [repository status](https://github.com/minio/minio) and [release notes](https://github.com/minio/minio/releases/latest). The local pinned source build is not a production provider recommendation. Reassess a maintained S3-compatible provider and vulnerability policy before implementing Phase 6 storage.

## Diagnostics and production configuration

Use safe ProblemDetails, correlation IDs, and structured logs without request payloads, passwords, access/refresh/reset/invitation tokens, connection strings, or customer note contents. Health endpoints expose minimal state; restrict operational access at deployment boundaries. Development OpenAPI and local HTTP do not imply production exposure policy. Audit important business changes durably and distinguish them from diagnostic logs with separate retention and access controls.

Allowlist exact web origins, methods, and headers. Never combine credentialed CORS with wildcard origins. Put production secrets in the deployment's secret manager, rotate signing/storage/database keys, use a least-privileged runtime database user, and separate the migration principal where supported. Backups require encryption, restoration drills, retention, and tenant/data-deletion review. No production readiness claim is justified until Phase 7 review and Phase 8 acceptance.

## Required adversarial verification

| Phase | Security scenarios |
| --- | --- |
| 0 | Safe unhealthy health output, no dependency checks in liveness, no secrets committed, safe global error contract |
| 1 | Invalid/expired tokens, refresh reuse/concurrency, logout/revocation, last-owner rules, invitation reuse/expiry/wrong recipient, inactive membership, role isolation, CSRF/CORS |
| 2 | Company A IDs against Company B reads/writes/relationships; technician unassigned order; hidden internal notes; tenant filter bypass paths; concurrent writes/assignments; archive history |
| 5 | Replayed operation after deactivation, operation payload mismatch, stolen/cross-scope cursor, authorization-scope change, stale versions, local logout cleanup |
| 6 | Cross-tenant object access, oversized/spoofed uploads, unauthorized hub group/resource, stale device association |
| 7–8 | Dependency/security review, rate-limit/abuse checks, proxy/HTTPS config, restore drill, log redaction, native offline protection, final documented limits |

## Admin web session boundary (Phase 3)

The web client keeps access credentials only in module memory. Refresh credentials remain in the existing Secure/HttpOnly/SameSite=Strict API cookie, scoped to `/api/v1/auth/web`. Theme preference is the only localStorage value. Every cookie mutation obtains a fresh CSRF token: anonymous for login/register/refresh and bearer-bound for logout/organization selection. HTTPS, server Origin checks, JWT session validation and database authorization remain authoritative.

A per-tab operation queue, a single-flight refresh promise and same-origin Web Locks serialize cookie refresh/login/logout/selection. A BroadcastChannel announces session changes without carrying credentials; other tabs clear their identity and bootstrap from the current cookie. Supported modern browsers provide Web Locks; older browsers fall back to per-tab serialization and backend replay detection still fails closed. A failed or ambiguous write is never automatically replayed. Requests have a bounded timeout and accept query cancellation.

Query caches are keyed by tenant, user, role and session. Organization switching cancels queries and clears the cache; a request generation guard discards late responses from the previous organization. Login/logout also clear cached data. Roles in `/auth/me` are reconciled on focus and periodically, while every backend request revalidates membership. UI guards are only a convenience: the existing owner/manager/resource policies, composite foreign keys and transactional concurrency checks enforce access on the server.

The UI renders strings as React text, including comments and audit metadata. It never injects user-supplied HTML or serializes whole entities into audit history. Existing append-only audit controls and before/after operational values are preserved.
