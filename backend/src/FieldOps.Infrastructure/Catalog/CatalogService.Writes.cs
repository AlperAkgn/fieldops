using FieldOps.Application.Catalog;
using FieldOps.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Catalog;

internal sealed partial class CatalogService
{
    public async Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken ct)
    {
        await using var transaction = await writes.BeginAsync(ct);
        var actor = RequireWriter();
        var type = request.CustomerType ?? throw Invalid("Customer type is required.");
        var item = ApplyRules(() => Customer.Create(actor.TenantId,
            new CustomerDetails(type, request.Name, request.Email, request.Phone, request.Notes), writes.Now));
        database.Customers.Add(item);
        writes.Audit("customer", item.Id, "created");
        await CommitAsync(transaction, ct);
        return ToCustomer(item);
    }

    public async Task<CustomerDto> UpdateCustomerAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct)
    {
        await using var transaction = await writes.BeginAsync(ct);
        var item = await Scoped(database.Customers, RequireWriter().TenantId).SingleOrDefaultAsync(item => item.Id == id, ct)
            ?? throw NotFound();
        ValidateUpdate(request, item.Version, item.IsActive);
        var type = request.Resolve<CustomerType?>(nameof(request.CustomerType), request.CustomerType, item.CustomerType)
            ?? throw Invalid("Customer type is required.");
        ApplyRules(() => item.Update(new CustomerDetails(type,
            request.Resolve(nameof(request.Name), request.Name, item.Name),
            request.Resolve(nameof(request.Email), request.Email, item.Email),
            request.Resolve(nameof(request.Phone), request.Phone, item.Phone),
            request.Resolve(nameof(request.Notes), request.Notes, item.Notes)), writes.Now));
        writes.Audit("customer", id, "updated");
        await CommitAsync(transaction, ct);
        return ToCustomer(item);
    }

    public async Task ArchiveCustomerAsync(Guid id, long expectedVersion, CancellationToken ct)
    {
        await using var transaction = await writes.BeginAsync(ct);
        var item = await Scoped(database.Customers, RequireWriter().TenantId).SingleOrDefaultAsync(item => item.Id == id, ct)
            ?? throw NotFound();
        ValidateVersion(expectedVersion, item.Version);
        if (!item.IsActive) return;
        item.Archive(writes.Now);
        writes.Audit("customer", id, "archived");
        await CommitAsync(transaction, ct);
    }

    public async Task<ServiceLocationDto> CreateServiceLocationAsync(CreateServiceLocationRequest request, CancellationToken ct)
    {
        await using var transaction = await writes.BeginAsync(ct);
        var actor = RequireWriter();
        await RequireActiveCustomerAsync(actor.TenantId, request.CustomerId, ct);
        var item = ApplyRules(() => ServiceLocation.Create(actor.TenantId, request.CustomerId,
            new LocationDetails(request.Name, request.AddressLine, request.City, request.Country,
                request.StateOrProvince, request.PostalCode, request.Latitude, request.Longitude,
                request.ContactName, request.ContactPhone), writes.Now));
        database.ServiceLocations.Add(item);
        writes.Audit("service_location", item.Id, "created");
        await CommitAsync(transaction, ct);
        return ToLocation(item);
    }

    public async Task<ServiceLocationDto> UpdateServiceLocationAsync(Guid id, UpdateServiceLocationRequest request, CancellationToken ct)
    {
        await using var transaction = await writes.BeginAsync(ct);
        var tenantId = RequireWriter().TenantId;
        var item = await Scoped(database.ServiceLocations, tenantId).SingleOrDefaultAsync(item => item.Id == id, ct)
            ?? throw NotFound();
        ValidateUpdate(request, item.Version, item.IsActive);
        await RequireActiveCustomerAsync(tenantId, item.CustomerId, ct);
        ApplyRules(() => item.Update(new LocationDetails(
            request.Resolve(nameof(request.Name), request.Name, item.Name),
            request.Resolve(nameof(request.AddressLine), request.AddressLine, item.AddressLine),
            request.Resolve(nameof(request.City), request.City, item.City),
            request.Resolve(nameof(request.Country), request.Country, item.Country),
            request.Resolve(nameof(request.StateOrProvince), request.StateOrProvince, item.StateOrProvince),
            request.Resolve(nameof(request.PostalCode), request.PostalCode, item.PostalCode),
            request.Resolve(nameof(request.Latitude), request.Latitude, item.Latitude),
            request.Resolve(nameof(request.Longitude), request.Longitude, item.Longitude),
            request.Resolve(nameof(request.ContactName), request.ContactName, item.ContactName),
            request.Resolve(nameof(request.ContactPhone), request.ContactPhone, item.ContactPhone)), writes.Now));
        writes.Audit("service_location", id, "updated");
        await CommitAsync(transaction, ct);
        return ToLocation(item);
    }

    public async Task ArchiveServiceLocationAsync(Guid id, long expectedVersion, CancellationToken ct)
    {
        await using var transaction = await writes.BeginAsync(ct);
        var item = await Scoped(database.ServiceLocations, RequireWriter().TenantId).SingleOrDefaultAsync(item => item.Id == id, ct)
            ?? throw NotFound();
        ValidateVersion(expectedVersion, item.Version);
        if (!item.IsActive) return;
        item.Archive(writes.Now);
        writes.Audit("service_location", id, "archived");
        await CommitAsync(transaction, ct);
    }

    public async Task<AssetCategoryDto> CreateAssetCategoryAsync(CreateAssetCategoryRequest request, CancellationToken ct)
    {
        await using var transaction = await writes.BeginAsync(ct);
        var actor = RequireWriter();
        var item = ApplyRules(() => AssetCategory.Create(actor.TenantId, request.Name, request.Description, writes.Now));
        await RequireCategoryNameAvailableAsync(item, ct);
        database.AssetCategories.Add(item);
        writes.Audit("asset_category", item.Id, "created");
        await CommitAsync(transaction, ct);
        return ToCategory(item);
    }

    public async Task<AssetCategoryDto> UpdateAssetCategoryAsync(Guid id, UpdateAssetCategoryRequest request, CancellationToken ct)
    {
        await using var transaction = await writes.BeginAsync(ct);
        var item = await Scoped(database.AssetCategories, RequireWriter().TenantId).SingleOrDefaultAsync(item => item.Id == id, ct)
            ?? throw NotFound();
        ValidateUpdate(request, item.Version, item.IsActive);
        ApplyRules(() => item.Update(request.Resolve(nameof(request.Name), request.Name, item.Name),
            request.Resolve(nameof(request.Description), request.Description, item.Description), writes.Now));
        await RequireCategoryNameAvailableAsync(item, ct);
        writes.Audit("asset_category", id, "updated");
        await CommitAsync(transaction, ct);
        return ToCategory(item);
    }

    public async Task ArchiveAssetCategoryAsync(Guid id, long expectedVersion, CancellationToken ct)
    {
        await using var transaction = await writes.BeginAsync(ct);
        var item = await Scoped(database.AssetCategories, RequireWriter().TenantId).SingleOrDefaultAsync(item => item.Id == id, ct)
            ?? throw NotFound();
        ValidateVersion(expectedVersion, item.Version);
        if (!item.IsActive) return;
        item.Archive(writes.Now);
        writes.Audit("asset_category", id, "archived");
        await CommitAsync(transaction, ct);
    }

    public async Task<AssetDto> CreateAssetAsync(CreateAssetRequest request, CancellationToken ct)
    {
        await using var transaction = await writes.BeginAsync(ct);
        var actor = RequireWriter();
        await RequireActiveLocationAsync(actor.TenantId, request.CustomerId, request.ServiceLocationId, ct);
        var item = ApplyRules(() => Asset.Create(actor.TenantId, request.CustomerId, request.ServiceLocationId,
            new AssetDetails(request.AssetCode, request.Name, request.Status, request.CategoryId, request.Brand, request.Model,
                request.SerialNumber, request.InstallationDate, request.WarrantyExpirationDate, request.Notes), writes.Now));
        await RequireActiveCategoryAsync(actor.TenantId, item.CategoryId, ct);
        await RequireAssetCodeAvailableAsync(item, ct);
        database.Assets.Add(item);
        writes.Audit("asset", item.Id, "created");
        await CommitAsync(transaction, ct);
        return ToAsset(item);
    }

    public async Task<AssetDto> UpdateAssetAsync(Guid id, UpdateAssetRequest request, CancellationToken ct)
    {
        await using var transaction = await writes.BeginAsync(ct);
        var tenantId = RequireWriter().TenantId;
        var item = await Scoped(database.Assets, tenantId).SingleOrDefaultAsync(item => item.Id == id, ct) ?? throw NotFound();
        ValidateUpdate(request, item.Version, item.IsActive);
        await RequireActiveLocationAsync(tenantId, item.CustomerId, item.ServiceLocationId, ct);
        var categoryId = request.Resolve(nameof(request.CategoryId), request.CategoryId, item.CategoryId);
        if (categoryId != item.CategoryId) await RequireActiveCategoryAsync(tenantId, categoryId, ct);
        var status = request.Resolve<AssetStatus?>(nameof(request.Status), request.Status, item.Status)
            ?? throw Invalid("Asset status is required.");
        ApplyRules(() => item.Update(new AssetDetails(
            request.Resolve(nameof(request.AssetCode), request.AssetCode, item.AssetCode),
            request.Resolve(nameof(request.Name), request.Name, item.Name), status, categoryId,
            request.Resolve(nameof(request.Brand), request.Brand, item.Brand),
            request.Resolve(nameof(request.Model), request.Model, item.Model),
            request.Resolve(nameof(request.SerialNumber), request.SerialNumber, item.SerialNumber),
            request.Resolve(nameof(request.InstallationDate), request.InstallationDate, item.InstallationDate),
            request.Resolve(nameof(request.WarrantyExpirationDate), request.WarrantyExpirationDate, item.WarrantyExpirationDate),
            request.Resolve(nameof(request.Notes), request.Notes, item.Notes)), writes.Now));
        await RequireAssetCodeAvailableAsync(item, ct);
        writes.Audit("asset", id, "updated");
        await CommitAsync(transaction, ct);
        return ToAsset(item);
    }

    public async Task ArchiveAssetAsync(Guid id, long expectedVersion, CancellationToken ct)
    {
        await using var transaction = await writes.BeginAsync(ct);
        var item = await Scoped(database.Assets, RequireWriter().TenantId).SingleOrDefaultAsync(item => item.Id == id, ct)
            ?? throw NotFound();
        ValidateVersion(expectedVersion, item.Version);
        if (!item.IsActive) return;
        item.Archive(writes.Now);
        writes.Audit("asset", id, "archived");
        await CommitAsync(transaction, ct);
    }

    private async Task RequireActiveCustomerAsync(Guid tenantId, Guid customerId, CancellationToken ct)
    {
        var parent = await Scoped(database.Customers, tenantId).AsNoTracking().SingleOrDefaultAsync(item => item.Id == customerId, ct)
            ?? throw NotFound();
        if (!parent.IsActive) throw Conflict("customer_archived", "The customer is archived.");
    }

    private async Task RequireActiveLocationAsync(Guid tenantId, Guid customerId, Guid locationId, CancellationToken ct)
    {
        await RequireActiveCustomerAsync(tenantId, customerId, ct);
        var parent = await Scoped(database.ServiceLocations, tenantId).AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == locationId && item.CustomerId == customerId, ct) ?? throw NotFound();
        if (!parent.IsActive) throw Conflict("service_location_archived", "The service location is archived.");
    }

    private async Task RequireActiveCategoryAsync(Guid tenantId, Guid? categoryId, CancellationToken ct)
    {
        if (categoryId is null) return;
        var parent = await Scoped(database.AssetCategories, tenantId).AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == categoryId, ct) ?? throw NotFound();
        if (!parent.IsActive) throw Conflict("asset_category_archived", "The asset category is archived.");
    }

    private async Task RequireCategoryNameAvailableAsync(AssetCategory item, CancellationToken ct)
    {
        if (await Scoped(database.AssetCategories, item.TenantId).AnyAsync(other =>
                other.Id != item.Id && other.NormalizedName == item.NormalizedName, ct))
            throw Conflict("asset_category_name_taken", "An asset category with this name already exists.");
    }

    private async Task RequireAssetCodeAvailableAsync(Asset item, CancellationToken ct)
    {
        if (await Scoped(database.Assets, item.TenantId).AnyAsync(other =>
                other.Id != item.Id && other.NormalizedAssetCode == item.NormalizedAssetCode, ct))
            throw Conflict("asset_code_taken", "An asset with this code already exists.");
    }
}
