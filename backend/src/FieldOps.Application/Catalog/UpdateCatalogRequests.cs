using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using FieldOps.Domain.Catalog;

namespace FieldOps.Application.Catalog;

public abstract class CatalogUpdateRequest
{
    private readonly HashSet<string> suppliedFields = new(StringComparer.Ordinal);

    public long ExpectedVersion { get; init; }

    [JsonIgnore]
    public bool HasChanges => suppliedFields.Count > 0;

    public T Resolve<T>(string fieldName, T updatedValue, T currentValue) =>
        suppliedFields.Contains(fieldName) ? updatedValue : currentValue;

    protected T Specify<T>(T value, [CallerMemberName] string fieldName = "")
    {
        suppliedFields.Add(fieldName);
        return value;
    }
}

public sealed class UpdateCustomerRequest : CatalogUpdateRequest
{
    public CustomerType? CustomerType { get; init => field = Specify(value); }
    public string? Name { get; init => field = Specify(value); }
    public string? Email { get; init => field = Specify(value); }
    public string? Phone { get; init => field = Specify(value); }
    public string? Notes { get; init => field = Specify(value); }
}

public sealed class UpdateServiceLocationRequest : CatalogUpdateRequest
{
    public string? Name { get; init => field = Specify(value); }
    public string? AddressLine { get; init => field = Specify(value); }
    public string? City { get; init => field = Specify(value); }
    public string? StateOrProvince { get; init => field = Specify(value); }
    public string? Country { get; init => field = Specify(value); }
    public string? PostalCode { get; init => field = Specify(value); }
    public decimal? Latitude { get; init => field = Specify(value); }
    public decimal? Longitude { get; init => field = Specify(value); }
    public string? ContactName { get; init => field = Specify(value); }
    public string? ContactPhone { get; init => field = Specify(value); }
}

public sealed class UpdateAssetCategoryRequest : CatalogUpdateRequest
{
    public string? Name { get; init => field = Specify(value); }
    public string? Description { get; init => field = Specify(value); }
}

public sealed class UpdateAssetRequest : CatalogUpdateRequest
{
    public string? AssetCode { get; init => field = Specify(value); }
    public string? Name { get; init => field = Specify(value); }
    public AssetStatus? Status { get; init => field = Specify(value); }
    public Guid? CategoryId { get; init => field = Specify(value); }
    public string? Brand { get; init => field = Specify(value); }
    public string? Model { get; init => field = Specify(value); }
    public string? SerialNumber { get; init => field = Specify(value); }
    public DateOnly? InstallationDate { get; init => field = Specify(value); }
    public DateOnly? WarrantyExpirationDate { get; init => field = Specify(value); }
    public string? Notes { get; init => field = Specify(value); }
}
