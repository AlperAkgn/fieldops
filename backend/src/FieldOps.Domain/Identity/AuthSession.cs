namespace FieldOps.Domain.Identity;

public sealed class AuthSession
{
    private AuthSession() { }
    public Guid Id { get; private set; }
    public Guid FamilyId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Transport { get; private set; } = "";
    public string SecurityStamp { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastUsedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevocationReason { get; private set; }

    public static AuthSession Create(Guid userId, Guid tenantId, string transport, string securityStamp,
        DateTimeOffset now, DateTimeOffset expiresAt, Guid? familyId = null)
    {
        var id = Guid.NewGuid();
        return new AuthSession
        {
            Id = id,
            FamilyId = familyId ?? id,
            UserId = userId,
            TenantId = tenantId,
            Transport = transport,
            SecurityStamp = securityStamp,
            CreatedAt = now,
            LastUsedAt = now,
            ExpiresAt = expiresAt
        };
    }

    public void Touch(DateTimeOffset now) => LastUsedAt = now;
    public void Revoke(DateTimeOffset now, string reason)
    {
        RevokedAt ??= now;
        RevocationReason ??= reason;
    }
}
