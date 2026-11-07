using FieldOps.Application.Catalog;
using FieldOps.Application.Operations;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Catalog;

internal sealed partial class CatalogService
{
    public Task<PageResult<CustomerDto>> ListCustomersAsync(CustomerListRequest request, CancellationToken ct)
    {
        var tenantId = RequireReader().TenantId;
        ValidateFilter(request.CustomerType);
        var query = FilterActive(Scoped(database.Customers.AsNoTracking(), tenantId), request.IsActive, request.IncludeArchived);
        if (request.CustomerType is CustomerType type) query = query.Where(item => item.CustomerType == type);
        if (SearchPattern(request.Search) is string pattern)
            query = query.Where(item => EF.Functions.ILike(item.Name, pattern)
                || (item.Email != null && EF.Functions.ILike(item.Email, pattern))
                || (item.Phone != null && EF.Functions.ILike(item.Phone, pattern)));
        return ReadPageAsync(Sort(query, request.Sort).Select(CustomerProjection), new PageRequest(request.Page, request.PageSize), ct);
    }

    public async Task<CustomerDto> GetCustomerAsync(Guid id, CancellationToken ct) =>
        await Scoped(database.Customers.AsNoTracking(), RequireReader().TenantId).Where(item => item.Id == id)
            .Select(CustomerProjection).SingleOrDefaultAsync(ct) ?? throw NotFound();

    public Task<PageResult<ServiceLocationDto>> ListServiceLocationsAsync(ServiceLocationListRequest request, CancellationToken ct)
    {
        var tenantId = RequireReader().TenantId;
        var query = FilterActive(Scoped(database.ServiceLocations.AsNoTracking(), tenantId), request.IsActive, request.IncludeArchived);
        if (request.CustomerId is Guid customerId) query = query.Where(item => item.CustomerId == customerId);
        if (SearchPattern(request.Search) is string pattern)
            query = query.Where(item => EF.Functions.ILike(item.Name, pattern) || EF.Functions.ILike(item.AddressLine, pattern)
                || EF.Functions.ILike(item.City, pattern) || (item.PostalCode != null && EF.Functions.ILike(item.PostalCode, pattern)));
        return ReadPageAsync(Sort(query, request.Sort).Select(LocationProjection), new PageRequest(request.Page, request.PageSize), ct);
    }

    public async Task<ServiceLocationDto> GetServiceLocationAsync(Guid id, CancellationToken ct) =>
        await Scoped(database.ServiceLocations.AsNoTracking(), RequireReader().TenantId).Where(item => item.Id == id)
            .Select(LocationProjection).SingleOrDefaultAsync(ct) ?? throw NotFound();

    public Task<PageResult<AssetCategoryDto>> ListAssetCategoriesAsync(AssetCategoryListRequest request, CancellationToken ct)
    {
        var tenantId = RequireReader().TenantId;
        var query = FilterActive(Scoped(database.AssetCategories.AsNoTracking(), tenantId), request.IsActive, request.IncludeArchived);
        if (SearchPattern(request.Search) is string pattern)
            query = query.Where(item => EF.Functions.ILike(item.Name, pattern));
        return ReadPageAsync(Sort(query, request.Sort).Select(CategoryProjection), new PageRequest(request.Page, request.PageSize), ct);
    }

    public async Task<AssetCategoryDto> GetAssetCategoryAsync(Guid id, CancellationToken ct) =>
        await Scoped(database.AssetCategories.AsNoTracking(), RequireReader().TenantId).Where(item => item.Id == id)
            .Select(CategoryProjection).SingleOrDefaultAsync(ct) ?? throw NotFound();

    public Task<PageResult<AssetDto>> ListAssetsAsync(AssetListRequest request, CancellationToken ct)
    {
        var tenantId = RequireReader().TenantId;
        ValidateFilter(request.Status);
        var query = FilterActive(Scoped(database.Assets.AsNoTracking(), tenantId), request.IsActive, request.IncludeArchived);
        if (request.CustomerId is Guid customerId) query = query.Where(item => item.CustomerId == customerId);
        if (request.ServiceLocationId is Guid locationId) query = query.Where(item => item.ServiceLocationId == locationId);
        if (request.CategoryId is Guid categoryId) query = query.Where(item => item.CategoryId == categoryId);
        if (request.Status is AssetStatus status) query = query.Where(item => item.Status == status);
        if (SearchPattern(request.Search) is string pattern)
            query = query.Where(item => EF.Functions.ILike(item.Name, pattern) || EF.Functions.ILike(item.AssetCode, pattern)
                || (item.Brand != null && EF.Functions.ILike(item.Brand, pattern))
                || (item.Model != null && EF.Functions.ILike(item.Model, pattern))
                || (item.SerialNumber != null && EF.Functions.ILike(item.SerialNumber, pattern)));
        return ReadPageAsync(Sort(query, request.Sort).Select(AssetProjection), new PageRequest(request.Page, request.PageSize), ct);
    }

    public async Task<AssetDto> GetAssetAsync(Guid id, CancellationToken ct) =>
        await Scoped(database.Assets.AsNoTracking(), RequireReader().TenantId).Where(item => item.Id == id)
            .Select(AssetProjection).SingleOrDefaultAsync(ct) ?? throw NotFound();

    public async Task<PageResult<AssetServiceHistoryItem>> GetAssetServiceHistoryAsync(Guid id, PageRequest page, CancellationToken ct)
    {
        var tenantId = RequireReader().TenantId;
        if (!await Scoped(database.Assets.AsNoTracking(), tenantId).AnyAsync(item => item.Id == id, ct)) throw NotFound();
        var history = Scoped(database.WorkOrders.AsNoTracking(), tenantId)
            .Where(item => item.AssetId == id && item.Status == WorkOrderStatus.Completed && item.ActualEndAt != null)
            .OrderByDescending(item => item.ActualEndAt).ThenBy(item => item.Id)
            .Select(item => new AssetServiceHistoryItem(item.Id, item.WorkOrderNumber, item.Title, item.ActualEndAt!.Value));
        return await ReadPageAsync(history, page, ct);
    }
}
