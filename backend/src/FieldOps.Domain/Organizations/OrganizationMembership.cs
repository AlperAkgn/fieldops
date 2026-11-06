namespace FieldOps.Domain.Organizations;

public sealed class OrganizationMembership : ITenantEntity
{
    private OrganizationMembership() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public OrganizationRole Role { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static OrganizationMembership Create(Guid tenantId, Guid userId, OrganizationRole role, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("Membership requires an organization and user.");
        }

        var membership = new OrganizationMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            CreatedAt = now.ToUniversalTime()
        };
        membership.ChangeRole(role, now);
        return membership;
    }

    public void ChangeRole(OrganizationRole role, DateTimeOffset now)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentException("A valid organization role is required.", nameof(role));
        }

        Role = role;
        UpdatedAt = now.ToUniversalTime();
    }

    public void SetActive(bool isActive, DateTimeOffset now)
    {
        IsActive = isActive;
        UpdatedAt = now.ToUniversalTime();
    }
}
