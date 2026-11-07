using FieldOps.Application.Catalog;
using FieldOps.Application.Operations;

namespace FieldOps.Api.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        var customers = app.MapGroup("/api/v1/customers").WithTags("Customers")
            .RequireAuthorization("CatalogRead").RequireRateLimiting("api").WithProblemResponses(400, 401, 403, 429);
        customers.MapGet("/", async ([AsParameters] CustomerListRequest query, ICatalogService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListCustomersAsync(query, ct)));
        customers.MapGet("/{id:guid}", async (Guid id, ICatalogService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetCustomerAsync(id, ct))).WithProblemResponses(404);
        customers.MapPost("/", async (CreateCustomerRequest request, ICatalogService service, CancellationToken ct) =>
        {
            var item = await service.CreateCustomerAsync(request, ct);
            return TypedResults.Created($"/api/v1/customers/{item.Id}", item);
        }).RequireAuthorization("OperationalWrite");
        customers.MapPatch("/{id:guid}", async (Guid id, UpdateCustomerRequest request, ICatalogService service, CancellationToken ct) =>
            TypedResults.Ok(await service.UpdateCustomerAsync(id, request, ct)))
            .RequireAuthorization("OperationalWrite").WithProblemResponses(404, 409);
        customers.MapDelete("/{id:guid}", async (Guid id, long expectedVersion, ICatalogService service, CancellationToken ct) =>
        {
            await service.ArchiveCustomerAsync(id, expectedVersion, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization("OperationalWrite").WithProblemResponses(404, 409);

        var locations = app.MapGroup("/api/v1/service-locations").WithTags("Service Locations")
            .RequireAuthorization("CatalogRead").RequireRateLimiting("api").WithProblemResponses(400, 401, 403, 429);
        locations.MapGet("/", async ([AsParameters] ServiceLocationListRequest query, ICatalogService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListServiceLocationsAsync(query, ct)));
        locations.MapGet("/{id:guid}", async (Guid id, ICatalogService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetServiceLocationAsync(id, ct))).WithProblemResponses(404);
        locations.MapPost("/", async (CreateServiceLocationRequest request, ICatalogService service, CancellationToken ct) =>
        {
            var item = await service.CreateServiceLocationAsync(request, ct);
            return TypedResults.Created($"/api/v1/service-locations/{item.Id}", item);
        }).RequireAuthorization("OperationalWrite").WithProblemResponses(404, 409);
        locations.MapPatch("/{id:guid}", async (Guid id, UpdateServiceLocationRequest request, ICatalogService service, CancellationToken ct) =>
            TypedResults.Ok(await service.UpdateServiceLocationAsync(id, request, ct)))
            .RequireAuthorization("OperationalWrite").WithProblemResponses(404, 409);
        locations.MapDelete("/{id:guid}", async (Guid id, long expectedVersion, ICatalogService service, CancellationToken ct) =>
        {
            await service.ArchiveServiceLocationAsync(id, expectedVersion, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization("OperationalWrite").WithProblemResponses(404, 409);

        var categories = app.MapGroup("/api/v1/asset-categories").WithTags("Asset Categories")
            .RequireAuthorization("CatalogRead").RequireRateLimiting("api").WithProblemResponses(400, 401, 403, 429);
        categories.MapGet("/", async ([AsParameters] AssetCategoryListRequest query, ICatalogService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAssetCategoriesAsync(query, ct)));
        categories.MapGet("/{id:guid}", async (Guid id, ICatalogService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAssetCategoryAsync(id, ct))).WithProblemResponses(404);
        categories.MapPost("/", async (CreateAssetCategoryRequest request, ICatalogService service, CancellationToken ct) =>
        {
            var item = await service.CreateAssetCategoryAsync(request, ct);
            return TypedResults.Created($"/api/v1/asset-categories/{item.Id}", item);
        }).RequireAuthorization("OperationalWrite").WithProblemResponses(409);
        categories.MapPatch("/{id:guid}", async (Guid id, UpdateAssetCategoryRequest request, ICatalogService service, CancellationToken ct) =>
            TypedResults.Ok(await service.UpdateAssetCategoryAsync(id, request, ct)))
            .RequireAuthorization("OperationalWrite").WithProblemResponses(404, 409);
        categories.MapDelete("/{id:guid}", async (Guid id, long expectedVersion, ICatalogService service, CancellationToken ct) =>
        {
            await service.ArchiveAssetCategoryAsync(id, expectedVersion, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization("OperationalWrite").WithProblemResponses(404, 409);

        var assets = app.MapGroup("/api/v1/assets").WithTags("Assets")
            .RequireAuthorization("CatalogRead").RequireRateLimiting("api").WithProblemResponses(400, 401, 403, 429);
        assets.MapGet("/", async ([AsParameters] AssetListRequest query, ICatalogService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAssetsAsync(query, ct)));
        assets.MapGet("/{id:guid}", async (Guid id, ICatalogService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAssetAsync(id, ct))).WithProblemResponses(404);
        assets.MapGet("/{id:guid}/service-history", async (Guid id, int? page, int? pageSize, ICatalogService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAssetServiceHistoryAsync(id, new PageRequest(page ?? 1, pageSize ?? 25), ct)))
            .WithProblemResponses(404);
        assets.MapPost("/", async (CreateAssetRequest request, ICatalogService service, CancellationToken ct) =>
        {
            var item = await service.CreateAssetAsync(request, ct);
            return TypedResults.Created($"/api/v1/assets/{item.Id}", item);
        }).RequireAuthorization("OperationalWrite").WithProblemResponses(404, 409);
        assets.MapPatch("/{id:guid}", async (Guid id, UpdateAssetRequest request, ICatalogService service, CancellationToken ct) =>
            TypedResults.Ok(await service.UpdateAssetAsync(id, request, ct)))
            .RequireAuthorization("OperationalWrite").WithProblemResponses(404, 409);
        assets.MapDelete("/{id:guid}", async (Guid id, long expectedVersion, ICatalogService service, CancellationToken ct) =>
        {
            await service.ArchiveAssetAsync(id, expectedVersion, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization("OperationalWrite").WithProblemResponses(404, 409);
    }
}
