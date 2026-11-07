using FieldOps.Domain.Catalog;

namespace FieldOps.UnitTests;

public sealed class CatalogDomainTests
{
    [Fact]
    public void CustomerValidationRejectsMalformedInputBeforeChangingTheExistingState()
    {
        var now = DateTimeOffset.UtcNow;
        var customer = Customer.Create(Guid.NewGuid(), new CustomerDetails(CustomerType.Business, "Çağrı 👩‍🔧"), now);
        Assert.Throws<ArgumentException>(() => customer.Update(
            new CustomerDetails(CustomerType.Business, "Changed", "not-an-email"), now));
        Assert.Throws<ArgumentException>(() => customer.Update(
            new CustomerDetails(CustomerType.Business, "Control\0name"), now));
        Assert.Equal("Çağrı 👩‍🔧", customer.Name);
        Assert.Equal(1, customer.Version);
        customer.Archive(now);
        Assert.Throws<InvalidOperationException>(() => customer.Update(
            new CustomerDetails(CustomerType.Individual, "Edited archive"), now));
    }

    [Theory]
    [InlineData(91, 29)]
    [InlineData(41, -181)]
    public void LocationRejectsOutOfRangeAndIncompleteCoordinates(decimal latitude, decimal longitude)
    {
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var details = new LocationDetails("Site", "Address", "City", "TR", Latitude: latitude, Longitude: longitude);
        Assert.Throws<ArgumentException>(() => ServiceLocation.Create(tenant, customer, details, now));
        Assert.Throws<ArgumentException>(() => ServiceLocation.Create(tenant, customer,
            details with { Latitude = 41, Longitude = null }, now));
    }

    [Fact]
    public void AssetNormalizesCodesAndRejectsInvalidWarrantyDatesBeforeMutation()
    {
        var now = DateTimeOffset.UtcNow;
        var details = new AssetDetails(" gen-01 ", "Generator", InstallationDate: new DateOnly(2025, 1, 1),
            WarrantyExpirationDate: new DateOnly(2026, 1, 1));
        var asset = Asset.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), details, now);
        Assert.Equal("GEN-01", asset.NormalizedAssetCode);
        Assert.Throws<ArgumentException>(() => asset.Update(details with
        {
            AssetCode = "Changed",
            WarrantyExpirationDate = new DateOnly(2024, 1, 1)
        }, now));
        Assert.Equal("gen-01", asset.AssetCode);
        Assert.Equal(1, asset.Version);
    }
}
