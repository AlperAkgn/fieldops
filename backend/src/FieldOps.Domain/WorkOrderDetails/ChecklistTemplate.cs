using FieldOps.Domain.Organizations;

namespace FieldOps.Domain.WorkOrderDetails;

public sealed class ChecklistTemplate : ITenantEntity
{
    public const int MaximumNameLength = 120;
    public const int MaximumItems = 100;

    private ChecklistTemplate() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = "";
    public bool IsActive { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ChecklistTemplate Create(Guid tenantId, string? name, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = DetailText.Required(name, MaximumNameLength, "invalid_template_name"),
        IsActive = true,
        Version = 1,
        CreatedAt = now,
        UpdatedAt = now
    };

    public void Rename(string? name, long expectedVersion, DateTimeOffset now)
    {
        var validated = DetailText.Required(name, MaximumNameLength, "invalid_template_name");
        Touch(expectedVersion, now);
        Name = validated;
    }

    public void Archive(long expectedVersion, DateTimeOffset now)
    {
        Touch(expectedVersion, now);
        IsActive = false;
    }

    public void Touch(long expectedVersion, DateTimeOffset now)
    {
        if (expectedVersion < 1)
            throw new DetailRuleException("invalid_version", "Provide a positive expected version.");
        if (Version != expectedVersion)
            throw new DetailRuleException("version_conflict", "The checklist template changed. Reload it and retry.", true);
        if (!IsActive)
            throw new DetailRuleException("checklist_template_archived", "An archived checklist template cannot be modified.", true);

        Version++;
        UpdatedAt = now;
    }
}
