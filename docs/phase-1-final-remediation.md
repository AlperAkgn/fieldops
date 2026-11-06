# Phase 1 final security remediation report

2026-11-07 · Europe/Istanbul

## 1. Root causes

- The selection routes originally required a live source session before checking the refresh credential. A successful switch retired that source, so a second request using the consumed credential returned 401 before family replay handling.
- Legacy registration could issue sessions before confirmation. The lineage migration only revoked sessions implicated in historical organization switches. Bearer and refresh validation omitted the activation conditions applied by password login.
- Login normalized and queried an email containing control characters. PostgreSQL rejected embedded NUL with an internal error.

## 2. Security fixes implemented

- Only the two selection endpoints carry verified JWT claims into the user-locked switch transaction. They retain signature, issuer, audience, lifetime and endpoint authentication checks. The transaction matches the source user, session, tenant, transport, security stamp and exact refresh hash before a replay can revoke the family. Issuance additionally requires a live source session and active source and destination memberships. The web path checks HTTPS, Origin and antiforgery before entering the transaction. [RFC 9700 §4.14.2](https://www.rfc-editor.org/rfc/rfc9700.html#section-4.14.2)
- Active account, confirmed email and a password hash are required for login, ordinary bearer validation, refresh and selection. Activation still confirms email and sets the mailbox owner's password in one transaction.
- Both login transports reject email control characters with 400 `invalid_email` before normalization and database lookup. Ordinary case normalization and existing Unicode email login remain supported.

## 3. Database and migration changes

`20261106210629_RevokeUnactivatedAccountSessions` is a forward-only data migration. It marks unexpired, unrevoked sessions for accounts with unconfirmed email or a missing password hash. Refresh credentials become unusable through their revoked parent sessions. It retains accounts, memberships, sessions, hashes and lineage and preserves existing revocations and activated accounts' sessions. The migration chain supports upgrades from both the initial Identity schema and the lineage schema. [Database and rollout procedure](database.md)

Upgrade and reapplication were exercised on disposable PostgreSQL 18.6 databases. The default development database was preserved. Back up and verify a restore of the database and Data Protection keys before applying the migration to an existing environment.

## 4. New regression tests

| Coverage | Cases | Before the fix |
| --- | ---: | --- |
| Native and web selection races, web refresh vs selection, A → B → A, exact credential binding, independent sessions, invalid or mismatched proofs and web CSRF/Origin | 5 | 4 failed, 1 control passed |
| Upgrades from both prior schema states and active-shaped sessions for unconfirmed or hashless accounts | 4 | 4 failed |
| Native and web control characters and supported Unicode email | 6 | 4 failed, 2 Unicode controls passed |

The final suite preserves the earlier 112 cases. The existing invitation test now expects 401 for an unconfirmed account's legacy bearer. It still checks wrong-recipient rejection, confirmation, successful acceptance and single use.

## 5. Test commands and results

| Command or check | Final result |
| --- | --- |
| `dotnet restore backend/FieldOps.slnx --locked-mode -p:NuGetAuditMode=all` | Passed without advisory warnings |
| `dotnet build backend/FieldOps.slnx -c Release --no-restore` and `dotnet format backend/FieldOps.slnx --verify-no-changes --no-restore` | Passed with zero build warnings and errors |
| `dotnet test backend/FieldOps.slnx -c Release --no-build` | 24 unit and 103 integration cases passed, zero failures and skips. Database cases used PostgreSQL 18.6 |
| Filtered native and web concurrency reruns | Four switch/switch and refresh/switch cases passed in each of three runs |
| `dotnet ef migrations has-pending-model-changes` with the API startup project and Infrastructure migration project | No model changes |
| Web and mobile npm workspace scripts | Lint, typechecks and 46 client tests passed. Web production build, Expo version check, Android/iOS export and Metro HTTP smoke passed |
| Isolated `docker compose up -d --build --wait`, trusted HTTPS smoke and `docker compose run --rm migrate` | Four migrations recorded and reapplication passed. HTTPS, CORS, CSRF, local SMTP, registration, login, refresh, logout and throttling passed. MinIO readiness returned 200 |
| `actionlint`, `docker compose config --quiet` and `git diff --check` | Passed |

## 6. Remaining issues

- Hosted CI, external SMTP delivery and physical-device execution remain outside the observed runs.
- npm audit reported 14 moderate tooling findings and zero high or critical findings. Deployment-wide quotas, retention, registration timing measurements and bulk tenant-write query costs remain hardening work.
- Source-membership changes by another administrator and an in-flight outgoing selection still use separate locks. This is an existing concurrency boundary. The destination membership determines the rights granted by a successful switch.

## 7. Phase 2 readiness

**READY.** All three findings from the independent Phase 1 re-audit passed their regressions in the integrated code. Phase 2 requires separate authorization.

## Multi-agent execution summary

| Role | Work and evidence |
| --- | --- |
| Replay | Scoped selection authentication, exact credential checks under the user lock, native and web race regressions and negative proof tests |
| Legacy migration | Additive data migration, bearer activation check and PostgreSQL upgrade and runtime regression tests |
| Independent QA | Read-only review of route scope, tenant authority, token binding, locking and migration safety. Confirmed the source-membership concurrency boundary predates this patch. The root agent ran the integrated runtime verification |
| Root | Native and web login tests and validation, activation conditions for login, refresh and selection, invitation contract update, integration, verification and documentation |

File ownership kept production edits separate. The replay agent handed off `IdentityService.cs` before the root agent added the activation and input checks.

## Files changed

- API and application — [ApiSecurity.cs](../backend/src/FieldOps.Api/Security/ApiSecurity.cs), [AuthEndpoints.cs](../backend/src/FieldOps.Api/Endpoints/AuthEndpoints.cs), [OrganizationEndpoints.cs](../backend/src/FieldOps.Api/Endpoints/OrganizationEndpoints.cs), [IdentityContracts.cs](../backend/src/FieldOps.Application/Identity/IdentityContracts.cs).
- Infrastructure — [IdentityService.cs](../backend/src/FieldOps.Infrastructure/Identity/IdentityService.cs), [SessionValidator.cs](../backend/src/FieldOps.Infrastructure/Authentication/SessionValidator.cs), [activation migration](../backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261106210629_RevokeUnactivatedAccountSessions.cs) and [migration designer](../backend/src/FieldOps.Infrastructure/Persistence/Migrations/20261106210629_RevokeUnactivatedAccountSessions.Designer.cs).
- Tests — [AuthenticationSecurityTests.cs](../backend/tests/FieldOps.IntegrationTests/AuthenticationSecurityTests.cs), [WebTransportSecurityTests.cs](../backend/tests/FieldOps.IntegrationTests/WebTransportSecurityTests.cs), [AccountRecoveryTests.cs](../backend/tests/FieldOps.IntegrationTests/AccountRecoveryTests.cs), [PostgreSqlMigrationTests.cs](../backend/tests/FieldOps.IntegrationTests/PostgreSqlMigrationTests.cs), [InvitationSecurityTests.cs](../backend/tests/FieldOps.IntegrationTests/InvitationSecurityTests.cs), [LegacyAccountSessionTests.cs](../backend/tests/FieldOps.IntegrationTests/LegacyAccountSessionTests.cs) and [LoginInputSecurityTests.cs](../backend/tests/FieldOps.IntegrationTests/LoginInputSecurityTests.cs).
- Documentation — this report, [implementation status](implementation-status.md), [security](security.md), [database](database.md), [API](api.md) and [initial remediation](phase-1-remediation.md).
