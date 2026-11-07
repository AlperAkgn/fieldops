using System.Linq.Expressions;
using FieldOps.Application.Catalog;
using FieldOps.Application.Common;
using FieldOps.Application.Operations;
using FieldOps.Application.Tenancy;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Organizations;
using FieldOps.Infrastructure.Operations;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FieldOps.Infrastructure.Catalog;

internal sealed partial class CatalogService(FieldOpsDbContext database, OperationalWriteContext writes) : ICatalogService
{
    private static readonly Expression<Func<Customer, CustomerDto>> CustomerProjection = item =>
        new CustomerDto(item.Id, item.CustomerType, item.Name, item.Email, item.Phone, item.Notes, item.IsActive,
            item.CreatedAt, item.UpdatedAt, item.Version);

    private static readonly Expression<Func<ServiceLocation, ServiceLocationDto>> LocationProjection = item =>
        new ServiceLocationDto(item.Id, item.CustomerId, item.Name, item.AddressLine, item.City, item.Country,
            item.StateOrProvince, item.PostalCode, item.Latitude, item.Longitude, item.ContactName, item.ContactPhone,
            item.IsActive, item.CreatedAt, item.UpdatedAt, item.Version);

    private static readonly Expression<Func<AssetCategory, AssetCategoryDto>> CategoryProjection = item =>
        new AssetCategoryDto(item.Id, item.Name, item.Description, item.IsActive, item.CreatedAt, item.UpdatedAt, item.Version);

    private static readonly Expression<Func<Asset, AssetDto>> AssetProjection = item =>
        new AssetDto(item.Id, item.CustomerId, item.ServiceLocationId, item.CategoryId, item.AssetCode, item.Name,
            item.Brand, item.Model, item.SerialNumber, item.InstallationDate, item.WarrantyExpirationDate, item.Status,
            item.Notes, item.IsActive, item.CreatedAt, item.UpdatedAt, item.Version);

    private static readonly Func<Customer, CustomerDto> ToCustomer = CustomerProjection.Compile();
    private static readonly Func<ServiceLocation, ServiceLocationDto> ToLocation = LocationProjection.Compile();
    private static readonly Func<AssetCategory, AssetCategoryDto> ToCategory = CategoryProjection.Compile();
    private static readonly Func<Asset, AssetDto> ToAsset = AssetProjection.Compile();

    private TenantActor RequireReader()
    {
        var actor = writes.Actor;
        if (actor.Role is not (OrganizationRole.OrganizationOwner or OrganizationRole.Manager or OrganizationRole.Viewer))
            throw new FieldOpsException(ErrorKind.Forbidden, "catalog_role_required", "A catalog read role is required.");
        return actor;
    }

    private TenantActor RequireWriter()
    {
        var actor = writes.Actor;
        if (actor.Role is not (OrganizationRole.OrganizationOwner or OrganizationRole.Manager))
            throw new FieldOpsException(ErrorKind.Forbidden, "catalog_write_role_required", "An owner or manager role is required.");
        return actor;
    }

    private static IQueryable<T> Scoped<T>(IQueryable<T> query, Guid tenantId) where T : class, ITenantEntity =>
        query.Where(item => item.TenantId == tenantId);

    private static IQueryable<T> FilterActive<T>(IQueryable<T> query, bool? isActive, bool includeArchived) where T : class
    {
        var active = isActive ?? (includeArchived ? null : true);
        return active is bool value ? query.Where(item => EF.Property<bool>(item, nameof(Customer.IsActive)) == value) : query;
    }

    private static string? SearchPattern(string? search)
    {
        if (search is null) return null;
        if (search.Any(char.IsControl) || search.Trim().Length > 120)
            throw Invalid("Search must contain at most 120 characters without control characters.");
        var term = search.Trim();
        if (term.Length == 0) return null;
        var literal = term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{literal}%";
    }

    private static IOrderedQueryable<T> Sort<T>(IQueryable<T> query, string? sort) where T : class
    {
        var ordered = sort switch
        {
            null or "" or "name" => query.OrderBy(item => EF.Property<string>(item, nameof(Customer.Name))),
            "-name" => query.OrderByDescending(item => EF.Property<string>(item, nameof(Customer.Name))),
            "createdAt" => query.OrderBy(item => EF.Property<DateTimeOffset>(item, nameof(Customer.CreatedAt))),
            "-createdAt" => query.OrderByDescending(item => EF.Property<DateTimeOffset>(item, nameof(Customer.CreatedAt))),
            "updatedAt" => query.OrderBy(item => EF.Property<DateTimeOffset>(item, nameof(Customer.UpdatedAt))),
            "-updatedAt" => query.OrderByDescending(item => EF.Property<DateTimeOffset>(item, nameof(Customer.UpdatedAt))),
            "assetCode" when typeof(T) == typeof(Asset) => query.OrderBy(item => EF.Property<string>(item, nameof(Asset.AssetCode))),
            "-assetCode" when typeof(T) == typeof(Asset) => query.OrderByDescending(item => EF.Property<string>(item, nameof(Asset.AssetCode))),
            "city" when typeof(T) == typeof(ServiceLocation) => query.OrderBy(item => EF.Property<string>(item, nameof(ServiceLocation.City))),
            "-city" when typeof(T) == typeof(ServiceLocation) => query.OrderByDescending(item => EF.Property<string>(item, nameof(ServiceLocation.City))),
            _ => throw Invalid("The requested sort field is not supported.")
        };
        return ordered.ThenBy(item => EF.Property<Guid>(item, nameof(Customer.Id)));
    }

    private static async Task<PageResult<T>> ReadPageAsync<T>(IQueryable<T> query, PageRequest page, CancellationToken ct)
    {
        page.Validate();
        var count = await query.CountAsync(ct);
        var items = await query.Skip(page.Offset).Take(page.PageSize).ToListAsync(ct);
        return new PageResult<T>(items, page.Page, page.PageSize, count);
    }

    private async Task CommitAsync(IDbContextTransaction transaction, CancellationToken ct)
    {
        try
        {
            await database.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict("version_conflict", "The resource changed. Reload it and retry with its current version.");
        }
    }

    private static void ValidateVersion(long expectedVersion, long currentVersion)
    {
        if (expectedVersion < 1) throw Invalid("Expected version must be positive.");
        if (expectedVersion != currentVersion)
            throw Conflict("version_conflict", "The resource changed. Reload it and retry with its current version.");
    }

    private static void ValidateUpdate(CatalogUpdateRequest request, long version, bool isActive)
    {
        ValidateVersion(request.ExpectedVersion, version);
        if (!request.HasChanges) throw Invalid("Provide at least one field to update.");
        if (!isActive) throw Conflict("catalog_archived", "An archived catalog resource cannot be updated.");
    }

    private static void ValidateFilter<T>(T? value) where T : struct, Enum
    {
        if (value is T supplied && !Enum.IsDefined(supplied)) throw Invalid("The requested filter value is not supported.");
    }

    private static T ApplyRules<T>(Func<T> operation)
    {
        try { return operation(); }
        catch (ArgumentException exception) { throw Invalid(exception.Message); }
    }

    private static void ApplyRules(Action operation)
    {
        try { operation(); }
        catch (ArgumentException exception) { throw Invalid(exception.Message); }
    }

    private static FieldOpsException Invalid(string message) => new(ErrorKind.Validation, "catalog_invalid", message);
    private static FieldOpsException NotFound() => new(ErrorKind.NotFound, "catalog_not_found", "The catalog resource was not found.");
    private static FieldOpsException Conflict(string code, string message) => new(ErrorKind.Conflict, code, message);
}
