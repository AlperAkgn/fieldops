# Phase 1 remediation report

2026-11-06 · Europe/Istanbul

The attached audit supplied the findings and the initial NOT READY verdict. The workspace contained the initial implementation report. This remediation keeps the Phase 1 scope and the existing foundation.

## Security vulnerabilities fixed

| Severity | Finding | Remediation |
| --- | --- | --- |
| High | A consumed refresh credential did not revoke a successor created by organization selection. | Both native and web selection consume the incoming refresh credential and preserve the family ID when issuing a new tenant-bound session. Replay revokes active descendants in the family. |
| Medium | The HTTP antiforgery endpoint produced an exception when issuing a Secure cookie. | Compose and Vite use the same trusted localhost development certificate on their HTTPS ports. Plain HTTP web authentication receives a safe 400. Secure cookies, exact HTTPS Origin allowlisting and CSRF checks remain enforced. |
| Medium | Valid signatures could trigger session database checks before rate limits. | The IP limiter runs before authentication and JWT session validation. Organization and invitation routes retain their authenticated user quota. Both limiters return ProblemDetails and Retry-After. |
| Medium | Registration exposed existing accounts through status and response shape. | Both transports return an identical, token-free 202 for valid new and existing addresses. The email recipient selects the password at confirmation. Login requires confirmed email. |
| Medium | PostgreSQL rejected a NUL in display names after persistence had begun. | Validation rejects control characters before opening the registration transaction and retains supported Unicode display names. |
| Low | OpenAPI omitted expected error responses. | Route metadata declares applicable 400, 401, 403, 404, 409 and 429 responses as `application/problem+json`. |

## Root causes

- Organization selection previously revoked the current session and minted an independent session. Replaying a credential could only address its original session. The family now spans switches, and replacement credentials retain their database-checked lineage. User-row locks serialize rotations and switches, with the target organization lock taken after the user lock. Authorization still derives the role from the target membership. [RFC 9700 refresh token guidance](https://www.rfc-editor.org/rfc/rfc9700.html#section-4.14.2)
- Secure antiforgery cookie generation requires a secure request. The old development listener was HTTP-only. Local Vite and the API now share the HTTPS scheme and localhost hostname so they are same-site for strict cookies. [Certificate setup](deployment.md#local-https-certificate)
- The old IP quota executed after JWT events. Signed requests could reach PostgreSQL regardless of available quota. The new IP stage precedes those events while preserving the organization endpoint policies.
- Registration formerly issued tokens for new addresses and returned a conflict for duplicate addresses. Ownership proof now occurs before initial password setup and sign-in. Normalized-email advisory locking preserves single-account creation. Duplicate registration leaves account state intact. Recovery retains its generic response.
- The original display-name check covered length and whitespace. Control-character validation covers NUL, C1 controls and trailing tabs before Identity or PostgreSQL is called.

## Database migrations

`20261106170229_RefreshSessionLineage` adds and backfills family IDs. Composite foreign keys bind root sessions to the same user and replacement credentials to the same family. A check and a unique index enforce coherent consumption and replacement links.

The old schema cannot reliably identify successors of past switches. The upgrade preserves all rows and revokes still-active sessions for accounts with recorded legacy organization switches. Other existing sessions remain intact. The upgrade test exercises a populated copy of the old schema, verifies retained data and selective revocation, checks foreign-key enforcement, and verifies that downgrade is refused. The migration was exercised only on disposable databases. [Migration and rollout details](database.md)

## Added regression tests

- Native and web refresh followed by a switch into another organization and replay of the original token. The descendant access and refresh tokens return 401. An independently signed-in native session remains valid, and the switched role retains its access restrictions.
- Concurrent refresh and selection with the same credential. Only one request issues a replacement and replay invalidates it. The existing concurrent-refresh and normal rotation tests remain enabled.
- Signed request throttling proves that rejection returns 429 and Retry-After without another session validation call.
- Native concurrent duplicate registration and web duplicate registration have the same public response, with no cookie or credentials. Activation tests cover pre-confirmation login denial, invalid password rollback, token account binding and replay.
- Display name NUL, C1 control, tab and blank input return 400 without user, organization or outbox rows. Supported Unicode is persisted.
- Owner listing page bounds and tenant authorization, route-specific OpenAPI error contracts, safe HTTP CSRF rejection, and trusted HTTPS Compose authentication.

## Unit and integration test results

| Check | Result |
| --- | --- |
| Locked .NET restore, Release build and format verification | PASS. Build reported zero warnings and errors. |
| Full `dotnet test` suite | PASS. 24 unit and 88 integration tests passed, zero failures or skips. Database cases used PostgreSQL 18.6. The same run included the Phase 0 foundation regression tests. |
| PostgreSQL migration checks | PASS. Fresh apply, populated upgrade, repeated apply, cross-family replacement rejection and guarded downgrade. |
| Web and mobile | PASS. Lint and strict typechecks, 23 web and 23 mobile tests, web production build, Expo compatibility, Android/iOS bundle export and Metro manifest/HTTP bundle smoke. |
| Workflow and Compose definition | PASS. `actionlint` and `docker compose config --quiet`. |

## Docker and HTTPS verification

**PASS.** The Linux Docker image built and the isolated Compose stack ran with PostgreSQL, Mailpit, the migration job and the API. The Python smoke connected using the public development certificate as a trust anchor and verified the localhost hostname. It passed HTTP CSRF rejection, HTTPS CSRF cookies, credentialed CORS preflight, registration without and with CSRF, real Mailpit delivery, activation, sign-in, refresh, logout, and signed-request 429 with Retry-After. The same smoke is wired into the infrastructure CI job.

Vite started on `https://localhost:5173`. Its served app module pointed to `https://localhost:5443`, and the API returned the matching CORS origin and healthy readiness. The isolated stack used synthetic accounts and separate database and key volumes. EF reported no pending model changes and migration reapplication succeeded. The existing default stack was left in place.

## Remaining issues

- **BLOCKED for external verification.** Hosted GitHub Actions, external SMTP delivery and native-device execution require separate environments. Local syntax, Mailpit delivery and Expo bundle checks ran.
- **Tenant write guard.** It reads the persisted tenant once per modified or deleted entity. Phase 1 ordinary writes touch a small number of scoped rows, and tests exercise forged tracked state. Profile and batch the guard before bulk tenant writes while retaining the persisted-row check.
- **Service size.** Identity and organization services concentrate transaction-sensitive workflows. Extract focused use cases when Phase 2 introduces new operations. Keep the locking and tenant invariants centralized.
- **Lock ordering.** Multi-scope writes take user locks before organization locks. Account deactivation takes organization locks in stable order. Organization-only owner operations keep their existing organization locks. Keep this ordering when adding multi-organization changes.
- **Read pagination and hardening.** Member and invitation endpoints now return bounded, stable pages. Offset pagination can move as rows change and should be profiled at large offsets. Rate limits are per instance. Deployment-wide throttling, session retention, and timing indistinguishability under adversarial measurement remain hardening work.

## Phase 2 readiness at the time of this report

**READY.** The replay regression, real PostgreSQL upgrade, HTTPS web authentication, quota behavior, registration privacy, input validation and Phase 0 regression checks passed. Phase 2 requires separate authorization.

The independent re-audit subsequently reproduced a separate selection replay path, legacy activation bypass and login input failure. The [final Phase 1 remediation report](phase-1-final-remediation.md) supersedes this decision.

## Files changed

5 added and 40 updated during remediation. The list compares against the Phase 1 working tree at the start of this task.

### Backend and migration

- [backend/src/FieldOps.Api/Endpoints/AuthEndpoints.cs](../backend/src/FieldOps.Api/Endpoints/AuthEndpoints.cs) · updated
- [backend/src/FieldOps.Api/Endpoints/OrganizationEndpoints.cs](../backend/src/FieldOps.Api/Endpoints/OrganizationEndpoints.cs) · updated
- [backend/src/FieldOps.Api/Endpoints/ProblemResponses.cs](../backend/src/FieldOps.Api/Endpoints/ProblemResponses.cs) · added
- [backend/src/FieldOps.Api/Program.cs](../backend/src/FieldOps.Api/Program.cs) · updated
- [backend/src/FieldOps.Api/Security/ApiSecurity.cs](../backend/src/FieldOps.Api/Security/ApiSecurity.cs) · updated
- [backend/src/FieldOps.Api/appsettings.Development.json](../backend/src/FieldOps.Api/appsettings.Development.json) · updated
- [backend/src/FieldOps.Application/Identity/IdentityContracts.cs](../backend/src/FieldOps.Application/Identity/IdentityContracts.cs) · updated
- [backend/src/FieldOps.Application/Organizations/IOrganizationService.cs](../backend/src/FieldOps.Application/Organizations/IOrganizationService.cs) · updated
- [backend/src/FieldOps.Domain/Identity/AuthSession.cs](../backend/src/FieldOps.Domain/Identity/AuthSession.cs) · updated
- [backend/src/FieldOps.Domain/Identity/RefreshCredential.cs](../backend/src/FieldOps.Domain/Identity/RefreshCredential.cs) · updated
- [backend/src/FieldOps.Infrastructure/Identity/IdentityService.cs](../backend/src/FieldOps.Infrastructure/Identity/IdentityService.cs) · updated
- [backend/src/FieldOps.Infrastructure/Organizations/OrganizationService.cs](../backend/src/FieldOps.Infrastructure/Organizations/OrganizationService.cs) · updated
- [backend/src/FieldOps.Infrastructure/Persistence/FieldOpsDbContext.cs](../backend/src/FieldOps.Infrastructure/Persistence/FieldOpsDbContext.cs) · updated
- [backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261106170229_RefreshSessionLineage.Designer.cs](../backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261106170229_RefreshSessionLineage.Designer.cs) · added
- [backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261106170229_RefreshSessionLineage.cs](../backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261106170229_RefreshSessionLineage.cs) · added
- [backend/src/FieldOps.Infrastructure/Persistence/Migrations/FieldOpsDbContextModelSnapshot.cs](../backend/src/FieldOps.Infrastructure/Persistence/Migrations/FieldOpsDbContextModelSnapshot.cs) · updated

### Regression tests

- [backend/tests/FieldOps.IntegrationTests/AccountRecoveryTests.cs](../backend/tests/FieldOps.IntegrationTests/AccountRecoveryTests.cs) · updated
- [backend/tests/FieldOps.IntegrationTests/ApiFactory.cs](../backend/tests/FieldOps.IntegrationTests/ApiFactory.cs) · updated
- [backend/tests/FieldOps.IntegrationTests/AuthenticationSecurityTests.cs](../backend/tests/FieldOps.IntegrationTests/AuthenticationSecurityTests.cs) · updated
- [backend/tests/FieldOps.IntegrationTests/EmailOutboxTests.cs](../backend/tests/FieldOps.IntegrationTests/EmailOutboxTests.cs) · updated
- [backend/tests/FieldOps.IntegrationTests/HealthEndpointTests.cs](../backend/tests/FieldOps.IntegrationTests/HealthEndpointTests.cs) · updated
- [backend/tests/FieldOps.IntegrationTests/IdentityTestBase.cs](../backend/tests/FieldOps.IntegrationTests/IdentityTestBase.cs) · updated
- [backend/tests/FieldOps.IntegrationTests/InvitationSecurityTests.cs](../backend/tests/FieldOps.IntegrationTests/InvitationSecurityTests.cs) · updated
- [backend/tests/FieldOps.IntegrationTests/OrganizationSecurityTests.cs](../backend/tests/FieldOps.IntegrationTests/OrganizationSecurityTests.cs) · updated
- [backend/tests/FieldOps.IntegrationTests/PostgreSqlMigrationTests.cs](../backend/tests/FieldOps.IntegrationTests/PostgreSqlMigrationTests.cs) · updated
- [backend/tests/FieldOps.IntegrationTests/WebTransportSecurityTests.cs](../backend/tests/FieldOps.IntegrationTests/WebTransportSecurityTests.cs) · updated
- [backend/tests/FieldOps.UnitTests/SessionDomainTests.cs](../backend/tests/FieldOps.UnitTests/SessionDomainTests.cs) · updated

### Web, Docker and CI

- [.dockerignore](../.dockerignore) · updated
- [.env.example](../.env.example) · updated
- [.github/workflows/ci.yml](../.github/workflows/ci.yml) · updated
- [.gitignore](../.gitignore) · updated
- [apps/admin-web/package.json](../apps/admin-web/package.json) · updated
- [apps/admin-web/src/App.tsx](../apps/admin-web/src/App.tsx) · updated
- [apps/admin-web/src/i18n/en.ts](../apps/admin-web/src/i18n/en.ts) · updated
- [apps/admin-web/vite.config.ts](../apps/admin-web/vite.config.ts) · updated
- [docker-compose.yml](../docker-compose.yml) · updated
- [infrastructure/scripts/smoke-web-auth.py](../infrastructure/scripts/smoke-web-auth.py) · added

### Documentation

- [README.md](../README.md) · updated
- [docs/api.md](api.md) · updated
- [docs/database.md](database.md) · updated
- [docs/deployment.md](deployment.md) · updated
- [docs/implementation-status.md](implementation-status.md) · updated
- [docs/phase-1-remediation.md](phase-1-remediation.md) · added
- [docs/phase-1-results.md](phase-1-results.md) · updated
- [docs/security.md](security.md) · updated
