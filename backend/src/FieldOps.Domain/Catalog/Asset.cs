using FieldOps.Domain.Organizations;

namespace FieldOps.Domain.Catalog;

public sealed class Asset : ITenantEntity
{
    private Asset() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid ServiceLocationId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string AssetCode { get; private set; } = "";
    public string NormalizedAssetCode { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? Brand { get; private set; }
    public string? Model { get; private set; }
    public string? SerialNumber { get; private set; }
    public DateOnly? InstallationDate { get; private set; }
    public DateOnly? WarrantyExpirationDate { get; private set; }
    public AssetStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    public static Asset Create(Guid tenantId, Guid customerId, Guid serviceLocationId, AssetDetails details, DateTimeOffset now)
    {
        CatalogFields.RequireId(tenantId, "Tenant");
        CatalogFields.RequireId(customerId, "Customer");
        CatalogFields.RequireId(serviceLocationId, "Service location");
        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customerId,
            ServiceLocationId = serviceLocationId,
            CreatedAt = now.ToUniversalTime()
        };
        asset.Update(details, now);
        return asset;
    }

    public void Update(AssetDetails details, DateTimeOffset now)
    {
        if (!IsActive) throw new InvalidOperationException("An archived asset cannot be edited.");
        if (!Enum.IsDefined(details.Status)) throw new ArgumentException("Asset status is invalid.");
        if (details.CategoryId == Guid.Empty) throw new ArgumentException("Asset category is invalid.");
        if (details.WarrantyExpirationDate < details.InstallationDate)
            throw new ArgumentException("Warranty expiration cannot precede installation.");
        var code = CatalogFields.Required(details.AssetCode, 64, "Asset code");
        var name = CatalogFields.Required(details.Name, 160, "Name");
        var brand = CatalogFields.Optional(details.Brand, 100, "Brand");
        var model = CatalogFields.Optional(details.Model, 100, "Model");
        var serialNumber = CatalogFields.Optional(details.SerialNumber, 120, "Serial number");
        var notes = CatalogFields.Optional(details.Notes, 4000, "Notes", multiline: true);

        CategoryId = details.CategoryId;
        AssetCode = code;
        NormalizedAssetCode = code.ToUpperInvariant();
        Name = name;
        Brand = brand;
        Model = model;
        SerialNumber = serialNumber;
        InstallationDate = details.InstallationDate;
        WarrantyExpirationDate = details.WarrantyExpirationDate;
        Status = details.Status;
        Notes = notes;
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

public sealed record AssetDetails(string? AssetCode, string? Name, AssetStatus Status = AssetStatus.Active,
    Guid? CategoryId = null, string? Brand = null, string? Model = null, string? SerialNumber = null,
    DateOnly? InstallationDate = null, DateOnly? WarrantyExpirationDate = null, string? Notes = null);
