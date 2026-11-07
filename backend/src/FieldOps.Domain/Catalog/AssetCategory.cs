using FieldOps.Domain.Organizations;

namespace FieldOps.Domain.Catalog;

public sealed class AssetCategory : ITenantEntity
{
    private AssetCategory() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = "";
    public string NormalizedName { get; private set; } = "";
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    public static AssetCategory Create(Guid tenantId, string? name, string? description, DateTimeOffset now)
    {
        CatalogFields.RequireId(tenantId, "Tenant");
        var category = new AssetCategory { Id = Guid.NewGuid(), TenantId = tenantId, CreatedAt = now.ToUniversalTime() };
        category.Update(name, description, now);
        return category;
    }

    public void Update(string? name, string? description, DateTimeOffset now)
    {
        if (!IsActive) throw new InvalidOperationException("An archived asset category cannot be edited.");
        var cleanName = CatalogFields.Required(name, 100, "Name");
        var cleanDescription = CatalogFields.Optional(description, 1000, "Description", multiline: true);
        Name = cleanName;
        NormalizedName = cleanName.ToUpperInvariant();
        Description = cleanDescription;
        UpdatedAt = now.ToUniversalTime();
        Version++;
    }

    public void Archive(DateTimeOffset now)
    {
        if (!IsActive) return;
        IsActive = false;
        UpdatedAt = now.ToUniversalTime();
        Version++;
    }
}
