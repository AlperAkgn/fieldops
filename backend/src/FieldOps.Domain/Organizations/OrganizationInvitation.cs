namespace FieldOps.Domain.Organizations;

public sealed class OrganizationInvitation : ITenantEntity
{
    private OrganizationInvitation() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CreatedByMembershipId { get; private set; }
    public string NormalizedEmail { get; private set; } = string.Empty;
    public OrganizationRole Role { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public Guid? AcceptedByMembershipId { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static OrganizationInvitation Create(Guid tenantId, Guid createdByMembershipId,
        string normalizedEmail, OrganizationRole role, string tokenHash, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || createdByMembershipId == Guid.Empty)
        {
            throw new ArgumentException("Invitation requires an organization and issuing membership.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (normalizedEmail.Length > 254 || tokenHash.Length != 64 || !tokenHash.All(char.IsAsciiHexDigit)
            || !Enum.IsDefined(role) || expiresAt <= now)
        {
            throw new ArgumentException("Invitation details are invalid.");
        }

        return new OrganizationInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CreatedByMembershipId = createdByMembershipId,
            NormalizedEmail = normalizedEmail,
            Role = role,
            TokenHash = tokenHash.ToUpperInvariant(),
            CreatedAt = now.ToUniversalTime(),
            ExpiresAt = expiresAt.ToUniversalTime()
        };
    }

    public bool CanAccept(DateTimeOffset now) => AcceptedAt is null && RevokedAt is null && ExpiresAt > now;

    public void Accept(Guid membershipId, DateTimeOffset now)
    {
        if (membershipId == Guid.Empty || !CanAccept(now))
        {
            throw new InvalidOperationException("Invitation is unavailable.");
        }

        AcceptedByMembershipId = membershipId;
        AcceptedAt = now.ToUniversalTime();
    }

    public void Revoke(DateTimeOffset now)
    {
        if (AcceptedAt is not null)
        {
            throw new InvalidOperationException("An accepted invitation cannot be revoked.");
        }

        RevokedAt ??= now.ToUniversalTime();
    }
}
