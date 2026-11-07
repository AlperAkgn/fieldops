using FieldOps.Domain.Catalog;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

public sealed partial class FieldOpsDbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<ServiceLocation> ServiceLocations => Set<ServiceLocation>();
    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();
    public DbSet<Asset> Assets => Set<Asset>();

    partial void ConfigureCatalog(ModelBuilder modelBuilder)
    {
        var customers = modelBuilder.Entity<Customer>();
        customers.ToTable("Customers", table =>
        {
            table.HasCheckConstraint("CK_Customers_Name", """length(btrim("Name")) > 0""");
            table.HasCheckConstraint("CK_Customers_Type", "\"CustomerType\" IN ('Individual', 'Business')");
            table.HasCheckConstraint("CK_Customers_Version", "\"Version\" > 0");
        });
        customers.HasKey(item => item.Id);
        customers.HasAlternateKey(item => new { item.TenantId, item.Id });
        customers.HasIndex(item => new { item.TenantId, item.IsActive, item.Name, item.Id });
        customers.HasOne<Organization>().WithMany().HasForeignKey(item => item.TenantId).OnDelete(DeleteBehavior.Restrict);
        customers.Property(item => item.CustomerType).HasConversion<string>().HasMaxLength(24);
        customers.Property(item => item.Name).HasMaxLength(160).IsRequired();
        customers.Property(item => item.Email).HasMaxLength(254);
        customers.Property(item => item.Phone).HasMaxLength(50);
        customers.Property(item => item.Notes).HasMaxLength(4000);
        customers.Property(item => item.Version).IsConcurrencyToken();
        customers.HasQueryFilter(item => IsSystemScope || item.TenantId == CurrentTenantId);

        var locations = modelBuilder.Entity<ServiceLocation>();
        locations.ToTable("ServiceLocations", table =>
        {
            table.HasCheckConstraint("CK_ServiceLocations_Fields",
                """length(btrim("Name")) > 0 AND length(btrim("AddressLine")) > 0 AND length(btrim("City")) > 0 AND length(btrim("Country")) > 0""");
            table.HasCheckConstraint("CK_ServiceLocations_Coordinates",
                """("Latitude" IS NULL) = ("Longitude" IS NULL) AND "Latitude" BETWEEN -90 AND 90 AND "Longitude" BETWEEN -180 AND 180""");
            table.HasCheckConstraint("CK_ServiceLocations_Version", "\"Version\" > 0");
        });
        locations.HasKey(item => item.Id);
        locations.HasAlternateKey(item => new { item.TenantId, item.Id });
        locations.HasAlternateKey(item => new { item.TenantId, item.CustomerId, item.Id });
        locations.HasIndex(item => new { item.TenantId, item.IsActive, item.Name, item.Id });
        locations.HasOne<Customer>().WithMany().HasForeignKey(item => new { item.TenantId, item.CustomerId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id }).OnDelete(DeleteBehavior.Restrict);
        locations.Property(item => item.Name).HasMaxLength(160).IsRequired();
        locations.Property(item => item.AddressLine).HasMaxLength(250).IsRequired();
        locations.Property(item => item.City).HasMaxLength(120).IsRequired();
        locations.Property(item => item.StateOrProvince).HasMaxLength(120);
        locations.Property(item => item.Country).HasMaxLength(100).IsRequired();
        locations.Property(item => item.PostalCode).HasMaxLength(25);
        locations.Property(item => item.ContactName).HasMaxLength(120);
        locations.Property(item => item.ContactPhone).HasMaxLength(50);
        locations.Property(item => item.Version).IsConcurrencyToken();
        locations.HasQueryFilter(item => IsSystemScope || item.TenantId == CurrentTenantId);

        var categories = modelBuilder.Entity<AssetCategory>();
        categories.ToTable("AssetCategories", table =>
        {
            table.HasCheckConstraint("CK_AssetCategories_Name",
                """length(btrim("Name")) > 0 AND length(btrim("NormalizedName")) > 0""");
            table.HasCheckConstraint("CK_AssetCategories_Version", "\"Version\" > 0");
        });
        categories.HasKey(item => item.Id);
        categories.HasAlternateKey(item => new { item.TenantId, item.Id });
        categories.HasIndex(item => new { item.TenantId, item.NormalizedName }).IsUnique();
        categories.HasOne<Organization>().WithMany().HasForeignKey(item => item.TenantId).OnDelete(DeleteBehavior.Restrict);
        categories.Property(item => item.Name).HasMaxLength(100).IsRequired();
        categories.Property(item => item.NormalizedName).HasMaxLength(100).IsRequired();
        categories.Property(item => item.Description).HasMaxLength(1000);
        categories.Property(item => item.Version).IsConcurrencyToken();
        categories.HasQueryFilter(item => IsSystemScope || item.TenantId == CurrentTenantId);

        var assets = modelBuilder.Entity<Asset>();
        assets.ToTable("Assets", table =>
        {
            table.HasCheckConstraint("CK_Assets_Fields",
                """length(btrim("AssetCode")) > 0 AND length(btrim("NormalizedAssetCode")) > 0 AND length(btrim("Name")) > 0""");
            table.HasCheckConstraint("CK_Assets_Status", "\"Status\" IN ('Active', 'UnderMaintenance', 'OutOfService', 'Retired')");
            table.HasCheckConstraint("CK_Assets_Dates", "\"WarrantyExpirationDate\" >= \"InstallationDate\"");
            table.HasCheckConstraint("CK_Assets_Version", "\"Version\" > 0");
        });
        assets.HasKey(item => item.Id);
        assets.HasAlternateKey(item => new { item.TenantId, item.Id });
        assets.HasAlternateKey(item => new { item.TenantId, item.CustomerId, item.ServiceLocationId, item.Id });
        assets.HasIndex(item => new { item.TenantId, item.NormalizedAssetCode }).IsUnique();
        assets.HasIndex(item => new { item.TenantId, item.IsActive, item.Name, item.Id });
        assets.HasOne<ServiceLocation>().WithMany().HasForeignKey(item => new { item.TenantId, item.CustomerId, item.ServiceLocationId })
            .HasPrincipalKey(item => new { item.TenantId, item.CustomerId, item.Id }).OnDelete(DeleteBehavior.Restrict);
        assets.HasOne<AssetCategory>().WithMany().HasForeignKey(item => new { item.TenantId, item.CategoryId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id }).OnDelete(DeleteBehavior.Restrict);
        assets.Property(item => item.AssetCode).HasMaxLength(64).IsRequired();
        assets.Property(item => item.NormalizedAssetCode).HasMaxLength(64).IsRequired();
        assets.Property(item => item.Name).HasMaxLength(160).IsRequired();
        assets.Property(item => item.Brand).HasMaxLength(100);
        assets.Property(item => item.Model).HasMaxLength(100);
        assets.Property(item => item.SerialNumber).HasMaxLength(120);
        assets.Property(item => item.Notes).HasMaxLength(4000);
        assets.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
        assets.Property(item => item.Version).IsConcurrencyToken();
        assets.HasQueryFilter(item => IsSystemScope || item.TenantId == CurrentTenantId);
    }
}
