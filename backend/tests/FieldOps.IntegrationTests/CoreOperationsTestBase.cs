using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Application.Identity;

namespace FieldOps.IntegrationTests;

public abstract class CoreOperationsTestBase : IdentityTestBase
{
    protected const string Root = "/api/v1";

    protected async Task<JsonNode> CallAsync(HttpMethod method, string path, TokenPair tokens, object? body = null,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await AuthorizedAsync(method, Root + path, tokens, body);
        Assert.Equal(expected, response.StatusCode);
        return response.StatusCode == HttpStatusCode.NoContent ? new JsonObject() : await ReadJsonAsync(response);
    }

    protected async Task<CatalogFixture> CatalogAsync(TokenPair tokens, string name = "Customer")
    {
        var customer = await CallAsync(HttpMethod.Post, "/customers", tokens,
            new { customerType = "Business", name, notes = "Private catalog notes" }, HttpStatusCode.Created);
        var customerId = Id(customer);
        var location = await CallAsync(HttpMethod.Post, "/service-locations", tokens,
            new
            {
                customerId,
                name = "Main site",
                addressLine = "1 Service Street",
                city = "Istanbul",
                country = "TR",
                latitude = 41m,
                longitude = 29m
            }, HttpStatusCode.Created);
        var locationId = Id(location);
        var category = await CallAsync(HttpMethod.Post, "/asset-categories", tokens,
            new { name = "Generators" }, HttpStatusCode.Created);
        var asset = await CallAsync(HttpMethod.Post, "/assets", tokens,
            new
            {
                customerId,
                serviceLocationId = locationId,
                categoryId = Id(category),
                assetCode = "GEN-001",
                name = "Generator",
                installationDate = "2025-01-01",
                warrantyExpirationDate = "2027-01-01"
            }, HttpStatusCode.Created);
        return new CatalogFixture(customerId, locationId, Id(asset), Id(category));
    }

    protected Task<JsonNode> OrderAsync(TokenPair tokens, CatalogFixture catalog, string title = "Inspection") =>
        CallAsync(HttpMethod.Post, "/work-orders", tokens,
            new
            {
                catalog.CustomerId,
                serviceLocationId = catalog.LocationId,
                catalog.AssetId,
                title,
                description = "Inspect the equipment",
                priority = "High"
            }, HttpStatusCode.Created);

    protected Task<JsonNode> ScheduleAsync(TokenPair tokens, JsonNode order, string start = "2027-01-10T09:00:00Z",
        string end = "2027-01-10T10:00:00Z", string? reason = null) =>
        CallAsync(HttpMethod.Post, $"/work-orders/{Id(order)}/schedule", tokens,
            new { expectedVersion = Version(order), plannedStartAt = start, plannedEndAt = end, overrideReason = reason });

    protected Task<JsonNode> AssignAsync(TokenPair tokens, JsonNode order, Guid technician, string? reason = null) =>
        CallAsync(HttpMethod.Post, $"/work-orders/{Id(order)}/assign", tokens,
            new
            {
                expectedVersion = Version(order),
                technicianUserIds = new[] { technician },
                primaryTechnicianUserId = technician,
                overrideReason = reason
            });

    protected static Guid Id(JsonNode value) => value["id"]!.GetValue<Guid>();
    protected static long Version(JsonNode value) => value["version"]!.GetValue<long>();
    protected sealed record CatalogFixture(Guid CustomerId, Guid LocationId, Guid AssetId, Guid CategoryId);
}
