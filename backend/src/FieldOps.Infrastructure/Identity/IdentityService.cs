using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using FieldOps.Application.Common;
using FieldOps.Application.Identity;
using FieldOps.Application.Tenancy;
using FieldOps.Domain.Identity;
using FieldOps.Domain.Organizations;
using FieldOps.Infrastructure.Authentication;
using FieldOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FieldOps.Infrastructure.Identity;

internal sealed class IdentityService(FieldOpsDbContext database, UserManager<FieldOpsUser> users,
    AccessTokenIssuer tokens, ISessionValidator sessions, IEmailOutbox mail, IOptions<JwtOptions> options,
    TimeProvider clock) : IIdentityService
{
    private static readonly Lazy<string> DummyPasswordHash = new(() =>
        new PasswordHasher<FieldOpsUser>(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }))
            .HashPassword(new FieldOpsUser(), "not-a-real-account-password"));

    public async Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (request is null) throw Invalid("invalid_registration", "Registration details are required.");
        database.ChangeTracker.Clear();
        using var system = database.BeginSystemScope();
        var email = ValidateEmail(request.Email);
        var allowedUserNameCharacters = users.Options.User.AllowedUserNameCharacters;
        if (!string.IsNullOrEmpty(allowedUserNameCharacters) && email.Any(character => !allowedUserNameCharacters.Contains(character)))
            throw Invalid("invalid_email", "Provide a supported email address.");
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 100
            || request.DisplayName.Any(char.IsControl))
            throw Invalid("invalid_display_name", "Display name must contain between 1 and 100 characters without control characters.");
        Organization organization;
        var now = clock.GetUtcNow();
        try { organization = Organization.Create(request.OrganizationName, request.TimeZoneId, now); }
        catch (Exception exception) when (exception is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw Invalid("invalid_organization", "Provide an organization name and a valid IANA time zone.");
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var normalized = users.NormalizeEmail(email);
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({normalized}, 1))", cancellationToken);
        if (await database.Users.AnyAsync(user => user.NormalizedEmail == normalized, cancellationToken))
            return;

        var user = new FieldOpsUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = now,
            IsActive = true,
            LockoutEnabled = true
        };
        EnsureIdentitySuccess(await users.CreateAsync(user));
        database.Organizations.Add(organization);
        database.OrganizationMemberships.Add(OrganizationMembership.Create(organization.Id, user.Id,
            OrganizationRole.OrganizationOwner, now));
        await QueueConfirmationAsync(user);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<TokenPair> LoginAsync(LoginRequest request, SessionTransport transport, CancellationToken cancellationToken)
    {
        if (request is null) throw AuthenticationFailed();
        if (request.Email?.Any(char.IsControl) == true)
            throw Invalid("invalid_email", "Provide an email address without control characters.");
        database.ChangeTracker.Clear();
        using var system = database.BeginSystemScope();
        ValidateTransport(transport);
        if (request.Password is null || request.Password.Length > 128 || request.Email is null || request.Email.Length > 254) throw AuthenticationFailed();
        var normalized = users.NormalizeEmail(request.Email?.Trim() ?? "");
        var userId = await database.Users.AsNoTracking().Where(user => user.NormalizedEmail == normalized)
            .Select(user => user.Id).SingleOrDefaultAsync(cancellationToken);
        if (userId == Guid.Empty)
        {
            VerifyDummyPassword(request.Password);
            throw AuthenticationFailed();
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(userId, cancellationToken);
        if (!CanUseSessions(user) || await users.IsLockedOutAsync(user))
        {
            VerifyDummyPassword(request.Password);
            throw AuthenticationFailed();
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            EnsureIdentitySuccess(await users.AccessFailedAsync(user));
            await transaction.CommitAsync(cancellationToken);
            throw AuthenticationFailed();
        }

        if (user.AccessFailedCount > 0) EnsureIdentitySuccess(await users.ResetAccessFailedCountAsync(user));
        var membership = await EligibleMemberships(user.Id)
            .Where(membership => request.OrganizationId == null || membership.TenantId == request.OrganizationId)
            .AsNoTracking().OrderBy(membership => membership.CreatedAt).ThenBy(membership => membership.Id).FirstOrDefaultAsync(cancellationToken);
        if (membership is null) throw AuthenticationFailed();
        var tenantId = membership.TenantId;
        await LockOrganizationAsync(tenantId, cancellationToken);
        membership = await EligibleMemberships(user.Id).AsNoTracking()
            .SingleOrDefaultAsync(member => member.TenantId == tenantId, cancellationToken);
        if (membership is null) throw AuthenticationFailed();
        var pair = CreateSession(user, membership.TenantId, membership.Role, transport, clock.GetUtcNow());
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return pair;
    }

    public async Task<TokenPair> RefreshAsync(string refreshToken, SessionTransport transport, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        using var system = database.BeginSystemScope();
        ValidateTransport(transport);
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 256) throw AuthenticationFailed();
        var hash = HashToken(refreshToken);
        var userId = await (from credential in database.RefreshCredentials.AsNoTracking()
                            join session in database.AuthSessions.AsNoTracking() on credential.SessionId equals session.Id
                            where credential.TokenHash == hash
                            select session.UserId).SingleOrDefaultAsync(cancellationToken);
        if (userId == Guid.Empty) throw AuthenticationFailed();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(userId, cancellationToken);
        var credentialRecord = await database.RefreshCredentials.SingleAsync(token => token.TokenHash == hash, cancellationToken);
        var sessionRecord = await database.AuthSessions.SingleAsync(session => session.Id == credentialRecord.SessionId, cancellationToken);
        if (sessionRecord.Transport != transport.ToString()) throw AuthenticationFailed();
        var now = clock.GetUtcNow();
        if (credentialRecord.ConsumedAt is not null)
        {
            await RevokeFamilyOnReplayAsync(sessionRecord, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw AuthenticationFailed();
        }

        var membership = await EligibleMemberships(userId).SingleOrDefaultAsync(member => member.TenantId == sessionRecord.TenantId, cancellationToken);
        if (!CanUseSessions(user) || sessionRecord.RevokedAt is not null || sessionRecord.ExpiresAt <= now ||
            credentialRecord.ExpiresAt <= now || sessionRecord.SecurityStamp != user.SecurityStamp || membership is null)
        {
            sessionRecord.Revoke(now, "session_invalid");
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw AuthenticationFailed();
        }

        var pair = CreateRefreshToken(sessionRecord, membership.Role, now, credentialRecord);
        sessionRecord.Touch(now);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return pair;
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(TenantActor actor, CancellationToken cancellationToken)
    {
        var current = await RequireActorAsync(actor, cancellationToken);
        using var system = database.BeginSystemScope();
        var user = await database.Users.AsNoTracking().SingleAsync(user => user.Id == current.UserId, cancellationToken);
        return new CurrentUserDto(user.Id, user.Email!, user.DisplayName, user.EmailConfirmed, current.TenantId, current.Role);
    }

    public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(TenantActor actor, CancellationToken cancellationToken)
    {
        var current = await RequireActorAsync(actor, cancellationToken);
        using var system = database.BeginSystemScope();
        var now = clock.GetUtcNow();
        var records = await database.AuthSessions.AsNoTracking().Where(session => session.UserId == current.UserId &&
            session.RevokedAt == null && session.ExpiresAt > now).OrderByDescending(session => session.CreatedAt)
            .Take(100).ToListAsync(cancellationToken);
        return records.Select(session => new SessionDto(session.Id, session.TenantId, Enum.Parse<SessionTransport>(session.Transport),
            session.CreatedAt, session.LastUsedAt, session.ExpiresAt, session.Id == current.SessionId)).ToArray();
    }

    public Task LogoutAsync(TenantActor actor, CancellationToken cancellationToken) =>
        RevokeSessionAsync(actor, actor.SessionId, cancellationToken);

    public Task LogoutWebAsync(TenantActor actor, string refreshToken, CancellationToken cancellationToken) =>
        RevokeSessionCoreAsync(actor, actor.SessionId, refreshToken, cancellationToken);

    public Task RevokeSessionAsync(TenantActor actor, Guid sessionId, CancellationToken cancellationToken) =>
        RevokeSessionCoreAsync(actor, sessionId, null, cancellationToken);

    private async Task RevokeSessionCoreAsync(TenantActor actor, Guid sessionId, string? webRefreshToken,
        CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await RequireActorAsync(actor, cancellationToken);
        using var system = database.BeginSystemScope();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(actor.UserId, cancellationToken) ?? throw AuthenticationFailed();
        await RequireCurrentSessionAsync(actor, user, cancellationToken);
        var session = await database.AuthSessions.SingleOrDefaultAsync(session => session.Id == sessionId && session.UserId == actor.UserId, cancellationToken)
            ?? throw new FieldOpsException(ErrorKind.NotFound, "session_not_found", "Session not found.");
        if (webRefreshToken is not null)
        {
            if (session.Transport != nameof(SessionTransport.Web)) throw AuthenticationFailed();
            await RequireMatchingCredentialAsync(session.Id, webRefreshToken, cancellationToken);
        }
        session.Revoke(clock.GetUtcNow(), "user_revoked");
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RevokeAllSessionsAsync(TenantActor actor, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await RequireActorAsync(actor, cancellationToken);
        using var system = database.BeginSystemScope();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(actor.UserId, cancellationToken) ?? throw AuthenticationFailed();
        await RequireCurrentSessionAsync(actor, user, cancellationToken);
        await RevokeUserSessionsAsync(user.Id, "all_sessions_revoked", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<TokenPair> SelectOrganizationAsync(SignedSession signedSession, Guid organizationId, SessionTransport transport,
        string refreshToken, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        using var system = database.BeginSystemScope();
        ValidateTransport(transport);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(signedSession.UserId, cancellationToken);
        if (!CanUseSessions(user)) throw AuthenticationFailed();
        var previous = await database.AuthSessions.SingleOrDefaultAsync(session =>
            session.Id == signedSession.SessionId && session.UserId == user.Id && session.TenantId == signedSession.TenantId &&
            session.Transport == transport.ToString() && session.SecurityStamp == user.SecurityStamp, cancellationToken)
            ?? throw AuthenticationFailed();
        var credential = await RequireMatchingCredentialAsync(previous.Id, refreshToken, cancellationToken);
        var now = clock.GetUtcNow();
        // A successful switch revokes the source session. Check exact consumed credentials before requiring a live source.
        if (credential.ConsumedAt is not null)
        {
            await RevokeFamilyOnReplayAsync(previous, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw AuthenticationFailed();
        }
        if (previous.RevokedAt is not null || previous.ExpiresAt <= now || credential.ExpiresAt <= now ||
            !await EligibleMemberships(user.Id).AnyAsync(member => member.TenantId == previous.TenantId, cancellationToken))
            throw AuthenticationFailed();
        if (!await EligibleMemberships(user.Id).AnyAsync(member => member.TenantId == organizationId, cancellationToken))
            throw new FieldOpsException(ErrorKind.NotFound, "organization_not_found", "Organization not found.");
        await LockOrganizationAsync(organizationId, cancellationToken);
        var membership = await EligibleMemberships(user.Id).SingleOrDefaultAsync(member => member.TenantId == organizationId, cancellationToken)
            ?? throw new FieldOpsException(ErrorKind.NotFound, "organization_not_found", "Organization not found.");
        now = clock.GetUtcNow();
        if (credential.ExpiresAt <= now || previous.ExpiresAt <= now) throw AuthenticationFailed();
        previous.Revoke(now, "organization_changed");
        var successor = AuthSession.Create(user.Id, organizationId, previous.Transport, user.SecurityStamp!, now,
            previous.ExpiresAt, previous.FamilyId);
        database.AuthSessions.Add(successor);
        var pair = CreateRefreshToken(successor, membership.Role, now, credential);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return pair;
    }

    private async Task<RefreshCredential> RequireMatchingCredentialAsync(Guid sessionId, string refreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 256) throw AuthenticationFailed();

        var hash = HashToken(refreshToken);
        return await database.RefreshCredentials.SingleOrDefaultAsync(credential =>
            credential.SessionId == sessionId && credential.TokenHash == hash, cancellationToken) ?? throw AuthenticationFailed();
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        using var system = database.BeginSystemScope();
        var address = ValidateEmail(email);
        var normalized = users.NormalizeEmail(address);
        var user = await database.Users.SingleOrDefaultAsync(user => user.NormalizedEmail == normalized && user.IsActive
            && user.EmailConfirmed, cancellationToken);
        if (user is null) return;
        var token = EncodeToken(await users.GeneratePasswordResetTokenAsync(user));
        var body = $"Reset your FieldOps password within one hour.\nUser ID: {user.Id}\nToken: {token}\nSubmit the userId, token, and newPassword to POST /api/v1/auth/reset-password.\nIgnore this message if you did not request it.";
        mail.Enqueue(new EmailMessage(address, "FieldOps password reset request", body));
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        using var system = database.BeginSystemScope();
        ValidatePasswordLength(newPassword);
        var decoded = DecodeToken(token);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(userId, cancellationToken);
        if (user is null || !user.IsActive || !user.EmailConfirmed) throw InvalidAccountToken();
        var result = await users.ResetPasswordAsync(user, decoded, newPassword);
        if (!result.Succeeded) throw Invalid("invalid_reset", "The token is invalid or expired, or the new password does not meet the password policy.");
        await RevokeUserSessionsAsync(user.Id, "password_reset", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RequestEmailConfirmationAsync(string email, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        using var system = database.BeginSystemScope();
        var address = ValidateEmail(email);
        var normalized = users.NormalizeEmail(address);
        var user = await database.Users.SingleOrDefaultAsync(user => user.NormalizedEmail == normalized && user.IsActive, cancellationToken);
        if (user is null || user.EmailConfirmed) return;
        await QueueConfirmationAsync(user);
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task ConfirmEmailAsync(Guid userId, string token, string password, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        using var system = database.BeginSystemScope();
        ValidatePasswordLength(password);
        var decoded = DecodeToken(token);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(userId, cancellationToken);
        if (user is null || !user.IsActive || user.EmailConfirmed) throw InvalidAccountToken();
        if (!(await users.ConfirmEmailAsync(user, decoded)).Succeeded) throw InvalidAccountToken();
        // Only the mailbox owner sets the initial password. This also replaces any legacy unconfirmed password.
        var passwordToken = await users.GeneratePasswordResetTokenAsync(user);
        if (!(await users.ResetPasswordAsync(user, passwordToken, password)).Succeeded)
            throw Invalid("invalid_password", "Use a password with upper and lowercase letters, a digit, and a symbol.");
        await RevokeUserSessionsAsync(user.Id, "email_confirmed", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeactivateAccountAsync(TenantActor actor, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await RequireActorAsync(actor, cancellationToken);
        using var system = database.BeginSystemScope();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(actor.UserId, cancellationToken) ?? throw AuthenticationFailed();
        await RequireCurrentSessionAsync(actor, user, cancellationToken);
        // Membership role/active changes use the organization lock, not the target user's lock.
        // Lock every existing membership scope first, including inactive memberships, then
        // read fresh state so a concurrent promotion/reactivation cannot bypass last-owner checks.
        var tenantIds = await database.OrganizationMemberships.AsNoTracking().Where(member => member.UserId == user.Id)
            .Select(member => member.TenantId).Distinct().ToListAsync(cancellationToken);
        var organizations = new Dictionary<Guid, Organization>();
        foreach (var tenantId in tenantIds.OrderBy(id => id.ToString("D")))
        {
            organizations[tenantId] = await database.Organizations.FromSqlInterpolated($"SELECT * FROM \"Organizations\" WHERE \"Id\" = {tenantId} FOR UPDATE")
                .SingleAsync(cancellationToken);
        }

        var memberships = await database.OrganizationMemberships.AsNoTracking()
            .Where(member => member.UserId == user.Id && member.IsActive).ToListAsync(cancellationToken);
        foreach (var tenantId in memberships.Select(member => member.TenantId).Distinct())
        {
            var isOwner = memberships.Any(member => member.TenantId == tenantId && member.Role == OrganizationRole.OrganizationOwner);
            if (organizations[tenantId].IsActive && isOwner && !await (from member in database.OrganizationMemberships
                                                                       join owner in database.Users on member.UserId equals owner.Id
                                                                       where member.TenantId == tenantId && member.UserId != user.Id && member.IsActive &&
                                                                           member.Role == OrganizationRole.OrganizationOwner && owner.IsActive
                                                                       select member.Id).AnyAsync(cancellationToken))
                throw new FieldOpsException(ErrorKind.Conflict, "last_owner", "Transfer organization ownership before deactivating this account.");
        }

        user.IsActive = false;
        EnsureIdentitySuccess(await users.UpdateSecurityStampAsync(user));
        foreach (var membership in memberships)
        {
            database.OrganizationMemberships.Attach(membership);
            membership.SetActive(false, clock.GetUtcNow());
        }
        await RevokeUserSessionsAsync(user.Id, "account_deactivated", cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private IQueryable<OrganizationMembership> EligibleMemberships(Guid userId) =>
        from membership in database.OrganizationMemberships
        join organization in database.Organizations on membership.TenantId equals organization.Id
        where membership.UserId == userId && membership.IsActive && organization.IsActive
        select membership;

    private async Task<FieldOpsUser?> LockUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await database.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task LockOrganizationAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        _ = await database.Organizations.FromSqlInterpolated($"SELECT * FROM \"Organizations\" WHERE \"Id\" = {tenantId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<TenantActor> RequireActorAsync(TenantActor actor, CancellationToken cancellationToken) =>
        await sessions.ValidateSessionAsync(actor.UserId, actor.SessionId, actor.TenantId, cancellationToken) ?? throw AuthenticationFailed();

    private async Task<AuthSession> RequireCurrentSessionAsync(TenantActor actor, FieldOpsUser user, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (!CanUseSessions(user)) throw AuthenticationFailed();
        var session = await database.AuthSessions.SingleOrDefaultAsync(session => session.Id == actor.SessionId && session.UserId == user.Id &&
            session.TenantId == actor.TenantId && session.RevokedAt == null && session.ExpiresAt > now && session.SecurityStamp == user.SecurityStamp,
            cancellationToken);
        if (session is null || !await EligibleMemberships(user.Id).AnyAsync(member => member.TenantId == actor.TenantId, cancellationToken))
            throw AuthenticationFailed();
        return session;
    }

    private TokenPair CreateSession(FieldOpsUser user, Guid tenantId, OrganizationRole role, SessionTransport transport,
        DateTimeOffset now)
    {
        var session = AuthSession.Create(user.Id, tenantId, transport.ToString(), user.SecurityStamp!, now,
            now.AddDays(options.Value.AbsoluteSessionDays));
        database.AuthSessions.Add(session);
        return CreateRefreshToken(session, role, now);
    }

    private TokenPair CreateRefreshToken(AuthSession session, OrganizationRole role, DateTimeOffset now, RefreshCredential? previous = null)
    {
        var raw = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        var expiry = now.AddDays(options.Value.RefreshTokenDays);
        if (expiry > session.ExpiresAt) expiry = session.ExpiresAt;
        var credential = RefreshCredential.Create(session, HashToken(raw), now, expiry);
        database.RefreshCredentials.Add(credential);
        previous?.Consume(credential.Id, now);
        var access = tokens.Issue(session, role);
        return new TokenPair(access.Token, raw, access.ExpiresAt, expiry, session.Id, session.TenantId, role, session.UserId);
    }

    private Task<int> RevokeFamilyOnReplayAsync(AuthSession replayedSession, DateTimeOffset now, CancellationToken cancellationToken) =>
        database.AuthSessions.Where(session => session.UserId == replayedSession.UserId &&
                session.FamilyId == replayedSession.FamilyId && session.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(session => session.RevokedAt, (DateTimeOffset?)now)
                .SetProperty(session => session.RevocationReason, "refresh_reuse"), cancellationToken);

    private async Task RevokeUserSessionsAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await database.AuthSessions.Where(session => session.UserId == userId && session.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(session => session.RevokedAt, (DateTimeOffset?)now)
                .SetProperty(session => session.RevocationReason, reason), cancellationToken);
    }

    private async Task QueueConfirmationAsync(FieldOpsUser user)
    {
        var token = EncodeToken(await users.GenerateEmailConfirmationTokenAsync(user));
        mail.Enqueue(new EmailMessage(user.Email!, "Confirm your FieldOps email",
            $"Confirm your FieldOps email within one hour.\nUser ID: {user.Id}\nToken: {token}\nSubmit the userId, token, and your chosen password to POST /api/v1/auth/confirm-email.\nIgnore this message if you did not request it."));
    }

    private static string ValidateEmail(string? email)
    {
        var address = email?.Trim();
        if (string.IsNullOrEmpty(address) || address.Length > 254 || email!.Any(char.IsControl) || !MailAddress.TryCreate(address, out var parsed) ||
            !parsed.Address.Equals(address, StringComparison.OrdinalIgnoreCase))
            throw Invalid("invalid_email", "Provide a valid email address.");
        return address;
    }

    private static void ValidatePasswordLength(string? password)
    {
        if (password is null || password.Length is < 12 or > 128)
            throw Invalid("invalid_password", "Password must contain between 12 and 128 characters.");
    }

    private static void ValidateTransport(SessionTransport transport)
    {
        if (!Enum.IsDefined(transport)) throw Invalid("invalid_transport", "Invalid session transport.");
    }

    private static bool CanUseSessions([NotNullWhen(true)] FieldOpsUser? user) =>
        user is { IsActive: true, EmailConfirmed: true, PasswordHash.Length: > 0 };

    private static void VerifyDummyPassword(string password) =>
        _ = new PasswordHasher<FieldOpsUser>().VerifyHashedPassword(new FieldOpsUser(), DummyPasswordHash.Value, password);

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string EncodeToken(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    private static string DecodeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw InvalidAccountToken();
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch (FormatException) { throw InvalidAccountToken(); }
    }
    private static void EnsureIdentitySuccess(IdentityResult result)
    {
        if (!result.Succeeded) throw new FieldOpsException(ErrorKind.Conflict, "identity_conflict", "The account changed. Retry the operation.");
    }
    private static FieldOpsException Invalid(string code, string message) => new(ErrorKind.Validation, code, message);
    private static FieldOpsException InvalidAccountToken() => Invalid("invalid_account_token", "The account token is invalid or expired.");
    private static FieldOpsException AuthenticationFailed() => new(ErrorKind.Unauthenticated, "authentication_failed", "Authentication failed.");
}
