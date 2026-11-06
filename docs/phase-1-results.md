# Phase 1 implementation and verification

2026-11-06 · Europe/Istanbul

This report records the initial Phase 1 delivery. The later remediation changes the registration, organization switch, throttling and local HTTPS contracts. [Phase 1 remediation report](phase-1-remediation.md)

## Resume audit

The working tree already contained the four-layer identity slice, membership and invitation persistence, the Phase 1 migration, the email dispatcher, and the initial security tests. The remaining work covered browser session alignment, final verification, and current setup and API documentation. The Phase 0 foundation commit remains the comparison point for the file inventory.

The resume changes bind web logout to the matching bearer session and require its current refresh credential before either transport can switch organization. They also correct generated OpenAPI 201 responses and keep the outbox poller out of tests that deliberately make PostgreSQL unresponsive. The running Compose stack exercises the worker separately through SMTP.

## Delivered behavior

| Requirement | Implementation |
| --- | --- |
| Registration and login | Identity validates and hashes passwords. Registration creates the account, organization, first owner membership, session, and confirmation outbox message in one transaction. Normalized email is unique. Login failures use a common public error and Identity lockout. |
| Access tokens and current user | Signed JWTs carry subject, session, and tenant. The API validates signature, issuer, audience and expiry, then verifies current database state. The active membership supplies the effective role. |
| Rotation and revocation | Refresh credentials are opaque and stored as SHA-256 hashes. PostgreSQL serializes rotations for an account. Replay revokes the session family. Current-session logout, individual revocation and logout-all take effect at the next access validation. |
| Web and native transport | Web refresh lives in a Secure, HttpOnly, SameSite=Strict cookie. State-changing web auth routes verify Origin and antiforgery. Web logout verifies that the cookie matches the bearer session. Native token-pair endpoints enforce their own transport. |
| Password recovery and account state | Confirmation and reset use Identity tokens. Reset and deactivation revoke sessions. Deactivation checks the last owner invariant while holding the relevant organization locks. |
| Organization and member administration | Active membership controls list, select and read access. Owner policies gate settings, member roles, deactivation/reactivation and invitations. Switching requires the current refresh credential, revokes the previous family, and preserves the original absolute expiry. |
| Invitations | Single-use, hashed 72-hour credentials bind the recipient's verified email to a tenant role. The issuer must remain an active owner. The API exposes metadata and the dispatcher delivers the credential by email. |
| Isolation | Verified request tenant, explicit resource scoping, default EF filters, write guards, composite tenant foreign keys and uniqueness constraints enforce separate boundaries. |
| Delivery | A transactional outbox protects message bodies with Data Protection. The worker delivers via SMTP, retries transient failure and tracks failed delivery without logging message content. Local Compose captures messages in Mailpit. |

## Implementation decisions

- Access, refresh and absolute session lifetimes default to 10 minutes, seven days and 30 days. Applications must serialize refresh calls because concurrent reuse revokes the family.
- Organization ownership lives on membership records. The API revalidates the role from PostgreSQL, so role changes take effect without waiting for the previous JWT to expire.
- User row locks precede organization locks for identity and membership changes. Account deactivation locks the user's membership organizations in stable order, then reads fresh membership state for the owner check.
- The web API uses ASP.NET Core antiforgery in addition to exact Origin allowlisting. Authentication transport is an explicit endpoint and stored family attribute.
- Local Compose binds credential endpoints to loopback by default. A device test can choose a LAN interface through `API_BIND_ADDRESS`. JWT signing material lives in local configuration and the Data Protection key ring uses a named volume.
- The first-party credential API uses Identity and JWT bearer validation. This phase establishes client transport contracts. Product sign-in screens are scheduled for Phases 3 and 4. [JWT bearer validation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0) · [ASP.NET Core antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0) · [Refresh rotation and replay](https://datatracker.ietf.org/doc/html/rfc9700#section-4.14)

## Database change

`20261105205852_IdentityAndOrganizations` follows the empty `InitialFoundation` baseline. It creates the Identity user and support tables, organizations, memberships, invitations, session families, hashed refresh credentials, and the encrypted email outbox. The migration and its snapshot are included in the source inventory. [Database details](database.md)

## Verification

| Check | Result |
| --- | --- |
| Locked restore with NuGet audit mode `all` | Passed with zero advisory warnings. |
| .NET Release build and format verification | Passed with zero build warnings and errors. |
| `dotnet test backend/FieldOps.slnx -c Release --no-build` | 24 unit and 74 integration tests passed, zero failures and skips. Database cases used PostgreSQL 18.6 and isolated databases. |
| Migration | Initial baseline and upgrade verified in tests. Compose applied both migrations from scratch. The EF model had no pending changes and migration reapplication succeeded on the development stack. |
| Web and mobile | Lint and strict typecheck passed. 23 web and 23 mobile tests passed. Web production build, Expo package validation, Android/iOS export and live Metro smoke passed. |
| Local HTTP and SMTP | The Linux API image built and ran on ASP.NET Core 10.0.14. Liveness, readiness and OpenAPI returned HTTP 200. A fresh disposable stack registered and confirmed two users, delivered and accepted an invitation, denied cross-tenant access and an unauthorized role, switched organization with the current refresh credential, detected a replay and revoked its family, reset a password, logged in and logged out. |
| CI definition | `actionlint` and Compose configuration checks passed. |

The PostgreSQL suite covers malformed, invalid and expired bearer tokens, registration uniqueness, password lockout and recovery, concurrent rotation and replay, refresh transport separation, CSRF and Origin, mixed web session credentials, organization selection with missing or replayed credentials, invite expiry and concurrent acceptance, role changes, membership/account deactivation, last-owner races, EF write guards and cross-tenant foreign keys. SMTP failure and retry are tested with a recording adapter. The Compose run uses the real SMTP adapter.

## Environment and phase boundary

SMTP verification used synthetic accounts and local Mailpit capture. The disposable stack and its volumes were removed after the run. The default local stack remains healthy. External delivery requires deployment SMTP credentials. Hosted Actions and native device execution are outside the observed verification runs. Phase 2 remains the separately authorized core domain and API milestone.

Rate limiting is local to the API instance. Expired session rows and consumed refresh hashes remain in PostgreSQL for session history and replay detection. Deployment-wide throttling and retention policy are tracked with production hardening.

## Source inventory

39 new files and 28 modified files compared with the Phase 0 foundation.

### Backend source and migrations

- [backend/src/FieldOps.Api/Endpoints/AuthEndpoints.cs](../backend/src/FieldOps.Api/Endpoints/AuthEndpoints.cs) · new
- [backend/src/FieldOps.Api/Endpoints/OrganizationEndpoints.cs](../backend/src/FieldOps.Api/Endpoints/OrganizationEndpoints.cs) · new
- [backend/src/FieldOps.Api/Errors/GlobalExceptionHandler.cs](../backend/src/FieldOps.Api/Errors/GlobalExceptionHandler.cs) · modified
- [backend/src/FieldOps.Api/FieldOps.Api.csproj](../backend/src/FieldOps.Api/FieldOps.Api.csproj) · modified
- [backend/src/FieldOps.Api/Program.cs](../backend/src/FieldOps.Api/Program.cs) · modified
- [backend/src/FieldOps.Api/Security/ApiSecurity.cs](../backend/src/FieldOps.Api/Security/ApiSecurity.cs) · new
- [backend/src/FieldOps.Api/packages.lock.json](../backend/src/FieldOps.Api/packages.lock.json) · modified
- [backend/src/FieldOps.Application/Common/FieldOpsException.cs](../backend/src/FieldOps.Application/Common/FieldOpsException.cs) · new
- [backend/src/FieldOps.Application/Identity/IdentityContracts.cs](../backend/src/FieldOps.Application/Identity/IdentityContracts.cs) · new
- [backend/src/FieldOps.Application/Organizations/IOrganizationService.cs](../backend/src/FieldOps.Application/Organizations/IOrganizationService.cs) · new
- [backend/src/FieldOps.Application/Tenancy/TenantContext.cs](../backend/src/FieldOps.Application/Tenancy/TenantContext.cs) · new
- [backend/src/FieldOps.Domain/Identity/AuthSession.cs](../backend/src/FieldOps.Domain/Identity/AuthSession.cs) · new
- [backend/src/FieldOps.Domain/Identity/RefreshCredential.cs](../backend/src/FieldOps.Domain/Identity/RefreshCredential.cs) · new
- [backend/src/FieldOps.Domain/Organizations/ITenantEntity.cs](../backend/src/FieldOps.Domain/Organizations/ITenantEntity.cs) · new
- [backend/src/FieldOps.Domain/Organizations/Organization.cs](../backend/src/FieldOps.Domain/Organizations/Organization.cs) · new
- [backend/src/FieldOps.Domain/Organizations/OrganizationInvitation.cs](../backend/src/FieldOps.Domain/Organizations/OrganizationInvitation.cs) · new
- [backend/src/FieldOps.Domain/Organizations/OrganizationMembership.cs](../backend/src/FieldOps.Domain/Organizations/OrganizationMembership.cs) · new
- [backend/src/FieldOps.Domain/Organizations/OrganizationRole.cs](../backend/src/FieldOps.Domain/Organizations/OrganizationRole.cs) · new
- [backend/src/FieldOps.Infrastructure/Authentication/AccessTokenIssuer.cs](../backend/src/FieldOps.Infrastructure/Authentication/AccessTokenIssuer.cs) · new
- [backend/src/FieldOps.Infrastructure/Authentication/SessionValidator.cs](../backend/src/FieldOps.Infrastructure/Authentication/SessionValidator.cs) · new
- [backend/src/FieldOps.Infrastructure/DependencyInjection.cs](../backend/src/FieldOps.Infrastructure/DependencyInjection.cs) · modified
- [backend/src/FieldOps.Infrastructure/Email/EmailDispatcher.cs](../backend/src/FieldOps.Infrastructure/Email/EmailDispatcher.cs) · new
- [backend/src/FieldOps.Infrastructure/Email/EmailOutbox.cs](../backend/src/FieldOps.Infrastructure/Email/EmailOutbox.cs) · new
- [backend/src/FieldOps.Infrastructure/Email/EmailOutboxWorker.cs](../backend/src/FieldOps.Infrastructure/Email/EmailOutboxWorker.cs) · new
- [backend/src/FieldOps.Infrastructure/Email/OutboundEmail.cs](../backend/src/FieldOps.Infrastructure/Email/OutboundEmail.cs) · new
- [backend/src/FieldOps.Infrastructure/Email/SmtpEmailDelivery.cs](../backend/src/FieldOps.Infrastructure/Email/SmtpEmailDelivery.cs) · new
- [backend/src/FieldOps.Infrastructure/FieldOps.Infrastructure.csproj](../backend/src/FieldOps.Infrastructure/FieldOps.Infrastructure.csproj) · modified
- [backend/src/FieldOps.Infrastructure/Identity/FieldOpsUser.cs](../backend/src/FieldOps.Infrastructure/Identity/FieldOpsUser.cs) · new
- [backend/src/FieldOps.Infrastructure/Identity/IdentityService.cs](../backend/src/FieldOps.Infrastructure/Identity/IdentityService.cs) · new
- [backend/src/FieldOps.Infrastructure/Organizations/FieldOpsDbContext.Organizations.cs](../backend/src/FieldOps.Infrastructure/Organizations/FieldOpsDbContext.Organizations.cs) · new
- [backend/src/FieldOps.Infrastructure/Organizations/OrganizationService.cs](../backend/src/FieldOps.Infrastructure/Organizations/OrganizationService.cs) · new
- [backend/src/FieldOps.Infrastructure/Persistence/FieldOpsDbContext.cs](../backend/src/FieldOps.Infrastructure/Persistence/FieldOpsDbContext.cs) · modified
- [backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261105205852_IdentityAndOrganizations.Designer.cs](../backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261105205852_IdentityAndOrganizations.Designer.cs) · new
- [backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261105205852_IdentityAndOrganizations.cs](../backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261105205852_IdentityAndOrganizations.cs) · new
- [backend/src/FieldOps.Infrastructure/Persistence/Migrations/FieldOpsDbContextModelSnapshot.cs](../backend/src/FieldOps.Infrastructure/Persistence/Migrations/FieldOpsDbContextModelSnapshot.cs) · modified
- [backend/src/FieldOps.Infrastructure/packages.lock.json](../backend/src/FieldOps.Infrastructure/packages.lock.json) · modified

### Backend tests

- [backend/tests/FieldOps.IntegrationTests/AccountRecoveryTests.cs](../backend/tests/FieldOps.IntegrationTests/AccountRecoveryTests.cs) · new
- [backend/tests/FieldOps.IntegrationTests/ApiFactory.cs](../backend/tests/FieldOps.IntegrationTests/ApiFactory.cs) · modified
- [backend/tests/FieldOps.IntegrationTests/ApiSecurityBoundaryTests.cs](../backend/tests/FieldOps.IntegrationTests/ApiSecurityBoundaryTests.cs) · new
- [backend/tests/FieldOps.IntegrationTests/AuthenticationSecurityTests.cs](../backend/tests/FieldOps.IntegrationTests/AuthenticationSecurityTests.cs) · new
- [backend/tests/FieldOps.IntegrationTests/EmailOutboxTests.cs](../backend/tests/FieldOps.IntegrationTests/EmailOutboxTests.cs) · new
- [backend/tests/FieldOps.IntegrationTests/HealthEndpointTests.cs](../backend/tests/FieldOps.IntegrationTests/HealthEndpointTests.cs) · modified
- [backend/tests/FieldOps.IntegrationTests/IdentityTestBase.cs](../backend/tests/FieldOps.IntegrationTests/IdentityTestBase.cs) · new
- [backend/tests/FieldOps.IntegrationTests/InvitationSecurityTests.cs](../backend/tests/FieldOps.IntegrationTests/InvitationSecurityTests.cs) · new
- [backend/tests/FieldOps.IntegrationTests/OrganizationSecurityTests.cs](../backend/tests/FieldOps.IntegrationTests/OrganizationSecurityTests.cs) · new
- [backend/tests/FieldOps.IntegrationTests/PostgreSqlMigrationTests.cs](../backend/tests/FieldOps.IntegrationTests/PostgreSqlMigrationTests.cs) · modified
- [backend/tests/FieldOps.IntegrationTests/TestDatabase.cs](../backend/tests/FieldOps.IntegrationTests/TestDatabase.cs) · new
- [backend/tests/FieldOps.IntegrationTests/WebTransportSecurityTests.cs](../backend/tests/FieldOps.IntegrationTests/WebTransportSecurityTests.cs) · new
- [backend/tests/FieldOps.IntegrationTests/packages.lock.json](../backend/tests/FieldOps.IntegrationTests/packages.lock.json) · modified
- [backend/tests/FieldOps.UnitTests/OrganizationDomainTests.cs](../backend/tests/FieldOps.UnitTests/OrganizationDomainTests.cs) · new
- [backend/tests/FieldOps.UnitTests/SessionDomainTests.cs](../backend/tests/FieldOps.UnitTests/SessionDomainTests.cs) · new
- [backend/tests/FieldOps.UnitTests/packages.lock.json](../backend/tests/FieldOps.UnitTests/packages.lock.json) · modified

### Client foundations

- [apps/admin-web/src/i18n/en.ts](../apps/admin-web/src/i18n/en.ts) · modified
- [apps/mobile/src/i18n/en.ts](../apps/mobile/src/i18n/en.ts) · modified

### Local infrastructure and CI

- [.env.example](../.env.example) · modified
- [.github/workflows/ci.yml](../.github/workflows/ci.yml) · modified
- [docker-compose.yml](../docker-compose.yml) · modified
- [infrastructure/docker/Api.Dockerfile](../infrastructure/docker/Api.Dockerfile) · modified

### Engineering instructions and documentation

- [AGENTS.md](../AGENTS.md) · modified
- [README.md](../README.md) · modified
- [docs/api.md](api.md) · modified
- [docs/architecture.md](architecture.md) · modified
- [docs/database.md](database.md) · modified
- [docs/deployment.md](deployment.md) · modified
- [docs/implementation-status.md](implementation-status.md) · modified
- [docs/phase-1-results.md](phase-1-results.md) · new
- [docs/security.md](security.md) · modified
