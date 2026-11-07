using FieldOps.Domain.Organizations;

namespace FieldOps.Domain.Catalog;

public sealed class ServiceLocation : ITenantEntity
{
    private ServiceLocation() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string Name { get; private set; } = "";
    public string AddressLine { get; private set; } = "";
    public string City { get; private set; } = "";
    public string? StateOrProvince { get; private set; }
    public string Country { get; private set; } = "";
    public string? PostalCode { get; private set; }
    public decimal? Latitude { get; private set; }
    public decimal? Longitude { get; private set; }
    public string? ContactName { get; private set; }
    public string? ContactPhone { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    public static ServiceLocation Create(Guid tenantId, Guid customerId, LocationDetails details, DateTimeOffset now)
    {
        CatalogFields.RequireId(tenantId, "Tenant");
        CatalogFields.RequireId(customerId, "Customer");
        var location = new ServiceLocation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customerId,
            CreatedAt = now.ToUniversalTime()
        };
        location.Update(details, now);
        return location;
    }

    public void Update(LocationDetails details, DateTimeOffset now)
    {
        if (!IsActive) throw new InvalidOperationException("An archived service location cannot be edited.");
        if (details.Latitude.HasValue != details.Longitude.HasValue || details.Latitude is < -90 or > 90
            || details.Longitude is < -180 or > 180)
            throw new ArgumentException("Provide both coordinates within the latitude and longitude bounds, or omit both.");
        var name = CatalogFields.Required(details.Name, 160, "Name");
        var address = CatalogFields.Required(details.AddressLine, 250, "Address");
        var city = CatalogFields.Required(details.City, 120, "City");
        var state = CatalogFields.Optional(details.StateOrProvince, 120, "State or province");
        var country = CatalogFields.Required(details.Country, 100, "Country");
        var postalCode = CatalogFields.Optional(details.PostalCode, 25, "Postal code");
        var contactName = CatalogFields.Optional(details.ContactName, 120, "Contact name");
        var contactPhone = CatalogFields.Optional(details.ContactPhone, 50, "Contact phone");

        Name = name;
        AddressLine = address;
        City = city;
        StateOrProvince = state;
        Country = country;
        PostalCode = postalCode;
        Latitude = details.Latitude;
        Longitude = details.Longitude;
        ContactName = contactName;
        ContactPhone = contactPhone;
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

public sealed record LocationDetails(string? Name, string? AddressLine, string? City, string? Country,
    string? StateOrProvince = null, string? PostalCode = null, decimal? Latitude = null, decimal? Longitude = null,
    string? ContactName = null, string? ContactPhone = null);
