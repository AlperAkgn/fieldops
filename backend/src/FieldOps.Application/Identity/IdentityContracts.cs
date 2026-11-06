using FieldOps.Application.Tenancy;
using FieldOps.Domain.Organizations;

namespace FieldOps.Application.Identity;

public enum SessionTransport { Native, Web }

// A validated JWT identifies the historical session. The switch must verify the stored session and refresh proof.
public sealed record SignedSession(Guid UserId, Guid SessionId, Guid TenantId);

public sealed record RegisterRequest(string Email, string DisplayName,
    string OrganizationName, string TimeZoneId = "UTC");
public sealed record LoginRequest(string Email, string Password, Guid? OrganizationId = null);
public sealed record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt,
    DateTimeOffset RefreshExpiresAt, Guid SessionId, Guid TenantId, OrganizationRole Role, Guid UserId);
public sealed record CurrentUserDto(Guid Id, string Email, string DisplayName, bool EmailConfirmed,
    Guid ActiveOrganizationId, OrganizationRole Role);
public sealed record SessionDto(Guid Id, Guid TenantId, SessionTransport Transport, DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt, DateTimeOffset ExpiresAt, bool IsCurrent);

public interface IIdentityService
{
    Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<TokenPair> LoginAsync(LoginRequest request, SessionTransport transport, CancellationToken cancellationToken);
    Task<TokenPair> RefreshAsync(string refreshToken, SessionTransport transport, CancellationToken cancellationToken);
    Task LogoutAsync(TenantActor actor, CancellationToken cancellationToken);
    Task LogoutWebAsync(TenantActor actor, string refreshToken, CancellationToken cancellationToken);
    Task<CurrentUserDto> GetCurrentUserAsync(TenantActor actor, CancellationToken cancellationToken);
    Task<IReadOnlyList<SessionDto>> GetSessionsAsync(TenantActor actor, CancellationToken cancellationToken);
    Task RevokeSessionAsync(TenantActor actor, Guid sessionId, CancellationToken cancellationToken);
    Task RevokeAllSessionsAsync(TenantActor actor, CancellationToken cancellationToken);
    Task<TokenPair> SelectOrganizationAsync(SignedSession signedSession, Guid organizationId, SessionTransport transport,
        string refreshToken, CancellationToken cancellationToken);
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken);
    Task ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken);
    Task RequestEmailConfirmationAsync(string email, CancellationToken cancellationToken);
    Task ConfirmEmailAsync(Guid userId, string token, string password, CancellationToken cancellationToken);
    Task DeactivateAccountAsync(TenantActor actor, CancellationToken cancellationToken);
}

public interface ISessionValidator
{
    Task<TenantActor?> ValidateSessionAsync(Guid userId, Guid sessionId, Guid tenantId, CancellationToken cancellationToken);
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "FieldOps";
    public string Audience { get; set; } = "FieldOps.Clients";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 10;
    public int RefreshTokenDays { get; set; } = 7;
    public int AbsoluteSessionDays { get; set; } = 30;
}

public sealed record EmailMessage(string ToEmail, string Subject, string Body);

public interface IEmailDelivery
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public interface IEmailOutbox
{
    // Enqueues into the caller's DbContext transaction; the caller saves/commits.
    void Enqueue(EmailMessage message);
}

public interface IEmailDispatcher
{
    Task<int> DispatchPendingAsync(CancellationToken cancellationToken);
}
