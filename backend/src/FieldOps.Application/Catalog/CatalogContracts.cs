using FieldOps.Application.Operations;
using FieldOps.Domain.Catalog;

namespace FieldOps.Application.Catalog;

public sealed record CreateCustomerRequest(CustomerType? CustomerType, string Name,
    string? Email = null, string? Phone = null, string? Notes = null);

public sealed record CreateServiceLocationRequest(Guid CustomerId, string Name, string AddressLine, string City, string Country,
    string? StateOrProvince = null, string? PostalCode = null, decimal? Latitude = null, decimal? Longitude = null,
    string? ContactName = null, string? ContactPhone = null);

public sealed record CreateAssetCategoryRequest(string Name, string? Description = null);

public sealed record CreateAssetRequest(Guid CustomerId, Guid ServiceLocationId, string AssetCode, string Name,
    AssetStatus Status = AssetStatus.Active, Guid? CategoryId = null, string? Brand = null, string? Model = null,
    string? SerialNumber = null, DateOnly? InstallationDate = null, DateOnly? WarrantyExpirationDate = null, string? Notes = null);

public sealed record CustomerDto(Guid Id, CustomerType CustomerType, string Name, string? Email, string? Phone,
    string? Notes, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version);

public sealed record ServiceLocationDto(Guid Id, Guid CustomerId, string Name, string AddressLine, string City, string Country,
    string? StateOrProvince, string? PostalCode, decimal? Latitude, decimal? Longitude, string? ContactName, string? ContactPhone,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version);

public sealed record AssetCategoryDto(Guid Id, string Name, string? Description, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version);

public sealed record AssetDto(Guid Id, Guid CustomerId, Guid ServiceLocationId, Guid? CategoryId, string AssetCode, string Name,
    string? Brand, string? Model, string? SerialNumber, DateOnly? InstallationDate, DateOnly? WarrantyExpirationDate,
    AssetStatus Status, string? Notes, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version);

public sealed record AssetServiceHistoryItem(Guid WorkOrderId, string WorkOrderNumber, string Title,
    DateTimeOffset CompletedAt);

public sealed record CustomerListRequest(int Page = 1, int PageSize = 25, string? Search = null,
    string? Sort = null, CustomerType? CustomerType = null, bool? IsActive = null, bool IncludeArchived = false);

public sealed record ServiceLocationListRequest(int Page = 1, int PageSize = 25, string? Search = null,
    string? Sort = null, Guid? CustomerId = null, bool? IsActive = null, bool IncludeArchived = false);

public sealed record AssetCategoryListRequest(int Page = 1, int PageSize = 25, string? Search = null,
    string? Sort = null, bool? IsActive = null, bool IncludeArchived = false);

public sealed record AssetListRequest(int Page = 1, int PageSize = 25, string? Search = null,
    string? Sort = null, Guid? CustomerId = null, Guid? ServiceLocationId = null, Guid? CategoryId = null,
    AssetStatus? Status = null, bool? IsActive = null, bool IncludeArchived = false);

public interface ICatalogService
{
    Task<PageResult<CustomerDto>> ListCustomersAsync(CustomerListRequest request, CancellationToken cancellationToken);
    Task<CustomerDto> GetCustomerAsync(Guid id, CancellationToken cancellationToken);
    Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken);
    Task<CustomerDto> UpdateCustomerAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken);
    Task ArchiveCustomerAsync(Guid id, long expectedVersion, CancellationToken cancellationToken);

    Task<PageResult<ServiceLocationDto>> ListServiceLocationsAsync(ServiceLocationListRequest request, CancellationToken cancellationToken);
    Task<ServiceLocationDto> GetServiceLocationAsync(Guid id, CancellationToken cancellationToken);
    Task<ServiceLocationDto> CreateServiceLocationAsync(CreateServiceLocationRequest request, CancellationToken cancellationToken);
    Task<ServiceLocationDto> UpdateServiceLocationAsync(Guid id, UpdateServiceLocationRequest request, CancellationToken cancellationToken);
    Task ArchiveServiceLocationAsync(Guid id, long expectedVersion, CancellationToken cancellationToken);

    Task<PageResult<AssetCategoryDto>> ListAssetCategoriesAsync(AssetCategoryListRequest request, CancellationToken cancellationToken);
    Task<AssetCategoryDto> GetAssetCategoryAsync(Guid id, CancellationToken cancellationToken);
    Task<AssetCategoryDto> CreateAssetCategoryAsync(CreateAssetCategoryRequest request, CancellationToken cancellationToken);
    Task<AssetCategoryDto> UpdateAssetCategoryAsync(Guid id, UpdateAssetCategoryRequest request, CancellationToken cancellationToken);
    Task ArchiveAssetCategoryAsync(Guid id, long expectedVersion, CancellationToken cancellationToken);

    Task<PageResult<AssetDto>> ListAssetsAsync(AssetListRequest request, CancellationToken cancellationToken);
    Task<AssetDto> GetAssetAsync(Guid id, CancellationToken cancellationToken);
    Task<AssetDto> CreateAssetAsync(CreateAssetRequest request, CancellationToken cancellationToken);
    Task<AssetDto> UpdateAssetAsync(Guid id, UpdateAssetRequest request, CancellationToken cancellationToken);
    Task ArchiveAssetAsync(Guid id, long expectedVersion, CancellationToken cancellationToken);
    Task<PageResult<AssetServiceHistoryItem>> GetAssetServiceHistoryAsync(Guid id, PageRequest page, CancellationToken cancellationToken);
}
