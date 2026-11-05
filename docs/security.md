# Security design

## Current boundary

Phase 0 exposes operational health and development OpenAPI only. It does not implement identity, tenant data, authorization policies, file access, or sync. The controls below define requirements for those later phases; they are not a claim that a deployable multi-tenant product already exists. Current verification and environment limits are in [implementation-status.md](implementation-status.md).

Keep local example secrets in `.env.example`, real values outside version control, and client build variables limited to public configuration. Both `VITE_*` and `EXPO_PUBLIC_*` values are shipped to users. Never place database credentials, signing keys, storage secrets, or email credentials there.

## Identity and session decisions — Phase 1

- Use ASP.NET Core Identity password hashing, reset tokens, lockout/security primitives, and normalized unique user identity. Do not invent password cryptography or store plaintext passwords.
- Registration atomically creates the initial organization and its Owner membership. Authenticated users may create additional organizations. Invitation tokens are random, hashed, single-use, expiring, and bound to the intended verified email and scoped role.
- Issue short-lived JWT access tokens with issuer/audience/signature/expiry validation. Include session and selected organization context; validate current membership/session state so deactivation, role changes, and revocation take effect according to the tested revocation policy.
- Refresh tokens are high-entropy opaque values. Store only a one-way hash plus token family/session, expiry, consumed/revoked timestamps, and rotation link. Rotate in one database transaction. Reuse of a consumed token revokes the family. Serialize competing refreshes; clients perform a single refresh at a time.
- The web client keeps access tokens in memory and refresh credentials in a Secure, HttpOnly, appropriately scoped SameSite cookie. Prefer same-origin web/API deployment. Refresh/logout cookie endpoints validate Origin and antiforgery tokens; do not depend on CORS as CSRF protection. Do not store refresh tokens in localStorage.
- The native client stores refresh/session credentials in Expo SecureStore and keeps access tokens in memory. SecureStore uses platform security facilities, with platform-specific persistence and backup considerations described in the [official SecureStore documentation](https://docs.expo.dev/versions/latest/sdk/securestore/). Native and browser token transport modes are explicit, not selected by an untrusted arbitrary origin.
- Online logout revokes the current session and removes credentials/cache. Offline logout clears local credentials/cache immediately; it cannot promise immediate server-side revocation without network access. Remote/session revocation is a separate online capability, and refresh expiration still limits abandoned sessions.
- Use non-enumerating authentication/reset responses, bounded input, password policy, and per-client/account-aware rate limits. Configure production HTTPS and forwarded headers only for trusted proxies.

## Tenant and resource isolation — Phases 1–2

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
