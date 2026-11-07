using System.Net;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.IntegrationTests;

[Trait("Category", "Database")]
public sealed class CoreSecurityAndConcurrencyTests : CoreOperationsTestBase
{
    [Fact]
    public async Task ForeignResourcesUnassignedTechniciansAndForgedTenantFieldsCannotCrossBoundaries()
    {
        var owner = await RegisterAsync();
        var outsider = await RegisterAsync();
        var technician = await RegisterAsync();
        var execution = await JoinAsync(owner, technician, OrganizationRole.Technician);
        var catalog = await CatalogAsync(owner.Tokens);
        var foreign = await CatalogAsync(outsider.Tokens);
        var order = await OrderAsync(owner.Tokens, catalog);
        var id = Id(order);
        foreach (var path in new[]
        {
            $"/customers/{catalog.CustomerId}", $"/service-locations/{catalog.LocationId}",
            $"/assets/{catalog.AssetId}", $"/assets/{catalog.AssetId}/service-history",
            $"/asset-categories/{catalog.CategoryId}", $"/work-orders/{id}",
            $"/work-orders/{id}/comments", $"/work-orders/{id}/checklist", $"/work-orders/{id}/history"
        })
            await CallAsync(HttpMethod.Get, path, outsider.Tokens, expected: HttpStatusCode.NotFound);
        foreach (var suffix in new[] { "", "/comments", "/checklist", "/history" })
            await CallAsync(HttpMethod.Get, $"/work-orders/{id}" + suffix, execution, expected: HttpStatusCode.NotFound);
        var ownList = await CallAsync(HttpMethod.Get, "/work-orders", execution);
        Assert.Empty(ownList["items"]!.AsArray());
        await CallAsync(HttpMethod.Post, "/customers", owner.Tokens,
            new { customerType = "Business", name = "Spoof", tenantId = outsider.Tokens.TenantId }, HttpStatusCode.BadRequest);
        await CallAsync(HttpMethod.Patch, $"/customers/{catalog.CustomerId}", outsider.Tokens,
            new { name = "Stolen", expectedVersion = 1 }, HttpStatusCode.NotFound);
        await CallAsync(HttpMethod.Post, "/assets", owner.Tokens,
            new { customerId = catalog.CustomerId, serviceLocationId = foreign.LocationId, assetCode = "BAD", name = "Mismatch" },
            HttpStatusCode.NotFound);
        await CallAsync(HttpMethod.Post, "/work-orders", owner.Tokens,
            new
            {
                customerId = catalog.CustomerId,
                serviceLocationId = catalog.LocationId,
                assetId = foreign.AssetId,
                title = "Mismatch",
                priority = "Medium"
            }, HttpStatusCode.BadRequest);

        var customerFk = await Assert.ThrowsAsync<PostgresException>(() => InDatabaseAsync(context =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "ServiceLocations" SET "CustomerId" = {foreign.CustomerId} WHERE "Id" = {catalog.LocationId};
                """)));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, customerFk.SqlState);
        var assetFk = await Assert.ThrowsAsync<PostgresException>(() => InDatabaseAsync(context =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "WorkOrders" SET "AssetId" = {foreign.AssetId} WHERE "Id" = {id};
                """)));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, assetFk.SqlState);
    }

    [Fact]
    public async Task ConcurrentAssignmentsAndReschedulesSerializeAvailabilityChecksAndRecordOverrides()
    {
        var owner = await RegisterAsync();
        var technician = await RegisterAsync();
        await AcceptMembershipAsync(owner, technician, OrganizationRole.Technician);
        var catalog = await CatalogAsync(owner.Tokens);
        var first = await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog, "First"));
        var second = await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog, "Second"));
        var responses = await Task.WhenAll(
            AuthorizedAsync(HttpMethod.Post, Root + $"/work-orders/{Id(first)}/assign", owner.Tokens,
                new
                {
                    expectedVersion = Version(first),
                    technicianUserIds = new[] { technician.Tokens.UserId },
                    primaryTechnicianUserId = technician.Tokens.UserId
                }),
            AuthorizedAsync(HttpMethod.Post, Root + $"/work-orders/{Id(second)}/assign", owner.Tokens,
                new
                {
                    expectedVersion = Version(second),
                    technicianUserIds = new[] { technician.Tokens.UserId },
                    primaryTechnicianUserId = technician.Tokens.UserId
                }));
        Guid winnerId;
        try
        {
            var winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            winnerId = Id(await ReadJsonAsync(winner));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
        var loser = winnerId == Id(first) ? second : first;
        var overridden = await AssignAsync(owner.Tokens, loser, technician.Tokens.UserId, "Emergency coverage approved");
        var audit = await CallAsync(HttpMethod.Get, $"/audit-logs?resourceId={Id(loser)}&action=availability_overridden", owner.Tokens);
        Assert.Equal("Emergency coverage approved", audit["items"]![0]!["reason"]!.GetValue<string>());
        // Moving the second order to the adjacent interval is valid under [start,end).
        overridden = await ScheduleAsync(owner.Tokens, overridden, "2027-01-10T10:00:00Z", "2027-01-10T11:00:00Z");
        await CallAsync(HttpMethod.Post, $"/work-orders/{Id(overridden)}/schedule", owner.Tokens,
            new
            {
                expectedVersion = Version(overridden),
                plannedStartAt = "2027-01-10T09:30:00Z",
                plannedEndAt = "2027-01-10T10:30:00Z"
            }, HttpStatusCode.Conflict);

        // Both existing assignments move into one previously free interval concurrently.
        var currentWinner = (await CallAsync(HttpMethod.Get, $"/work-orders/{winnerId}", owner.Tokens))["order"]!;
        var schedules = await Task.WhenAll(
            AuthorizedAsync(HttpMethod.Post, Root + $"/work-orders/{winnerId}/schedule", owner.Tokens,
                new { expectedVersion = Version(currentWinner), plannedStartAt = "2027-01-10T12:00:00Z", plannedEndAt = "2027-01-10T13:00:00Z" }),
            AuthorizedAsync(HttpMethod.Post, Root + $"/work-orders/{Id(overridden)}/schedule", owner.Tokens,
                new { expectedVersion = Version(overridden), plannedStartAt = "2027-01-10T12:00:00Z", plannedEndAt = "2027-01-10T13:00:00Z" }));
        try
        {
            Assert.Single(schedules, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(schedules, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in schedules) response.Dispose();
        }
    }

    [Fact]
    public async Task AssignmentReplacementRetainsHistoryAndExactlyOnePrimary()
    {
        var owner = await RegisterAsync();
        var first = await RegisterAsync();
        var second = await RegisterAsync();
        await AcceptMembershipAsync(owner, first, OrganizationRole.Technician);
        await AcceptMembershipAsync(owner, second, OrganizationRole.Technician);
        var catalog = await CatalogAsync(owner.Tokens);
        var order = await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog));
        var path = $"/work-orders/{Id(order)}";
        order = await CallAsync(HttpMethod.Post, path + "/assign", owner.Tokens,
            new
            {
                expectedVersion = Version(order),
                technicianUserIds = new[] { first.Tokens.UserId, second.Tokens.UserId },
                primaryTechnicianUserId = first.Tokens.UserId
            });
        order = await CallAsync(HttpMethod.Post, path + "/assign", owner.Tokens,
            new
            {
                expectedVersion = Version(order),
                technicianUserIds = new[] { first.Tokens.UserId, second.Tokens.UserId },
                primaryTechnicianUserId = second.Tokens.UserId
            });
        var detail = await CallAsync(HttpMethod.Get, path, owner.Tokens);
        var assignments = detail["assignments"]!.AsArray();
        Assert.Equal(2, assignments.Count);
        var primary = Assert.Single(assignments, item => item!["isPrimary"]!.GetValue<bool>())!;
        Assert.Equal(second.Tokens.UserId, primary["technicianUserId"]!.GetValue<Guid>());
        Assert.True(await InDatabaseAsync(context => context.WorkOrderAssignments.IgnoreQueryFilters()
            .AnyAsync(item => item.WorkOrderId == Id(order) && item.RemovedAt != null)));
        await CallAsync(HttpMethod.Post, path + "/assign", owner.Tokens,
            new
            {
                expectedVersion = Version(order),
                technicianUserIds = new[] { owner.Tokens.UserId },
                primaryTechnicianUserId = owner.Tokens.UserId
            }, HttpStatusCode.BadRequest);
        await CallAsync(HttpMethod.Post, path + "/schedule", owner.Tokens,
            new { expectedVersion = Version(order), plannedStartAt = "2027-01-10T09:00:00", plannedEndAt = "2027-01-10T10:00:00" },
            HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CatalogPatchPaginationArchiveAndConcurrentVersionChecksWork()
    {
        var owner = await RegisterAsync();
        var catalog = await CatalogAsync(owner.Tokens);
        var path = $"/customers/{catalog.CustomerId}";
        var updated = await CallAsync(HttpMethod.Patch, path, owner.Tokens,
            new { expectedVersion = 1, email = "contact@example.test", phone = "+90 123" });
        updated = await CallAsync(HttpMethod.Patch, path, owner.Tokens,
            new { expectedVersion = Version(updated), email = (string?)null });
        Assert.Null(updated["email"]);
        Assert.Equal("+90 123", updated["phone"]!.GetValue<string>());
        var writes = await Task.WhenAll(
            AuthorizedAsync(HttpMethod.Patch, Root + path, owner.Tokens, new { expectedVersion = Version(updated), name = "Version A" }),
            AuthorizedAsync(HttpMethod.Patch, Root + path, owner.Tokens, new { expectedVersion = Version(updated), name = "Version B" }));
        try
        {
            Assert.Single(writes, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(writes, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in writes) response.Dispose();
        }
        await CallAsync(HttpMethod.Post, "/customers", owner.Tokens, new { customerType = "Individual", name = "Second" }, HttpStatusCode.Created);
        var list = await CallAsync(HttpMethod.Get, "/customers?pageSize=1&sort=name", owner.Tokens);
        Assert.Equal(2, list["totalCount"]!.GetValue<int>());
        Assert.Single(list["items"]!.AsArray());
        var next = await CallAsync(HttpMethod.Get, "/customers?pageSize=1&page=2&sort=name", owner.Tokens);
        Assert.NotEqual(Id(list["items"]![0]!), Id(next["items"]![0]!));
        await CallAsync(HttpMethod.Get, "/customers?sort=secret", owner.Tokens, expected: HttpStatusCode.BadRequest);
        await CallAsync(HttpMethod.Get, "/customers?pageSize=101", owner.Tokens, expected: HttpStatusCode.BadRequest);
        var current = await CallAsync(HttpMethod.Get, path, owner.Tokens);
        await CallAsync(HttpMethod.Delete, path + $"?expectedVersion={Version(current)}", owner.Tokens, expected: HttpStatusCode.NoContent);
        list = await CallAsync(HttpMethod.Get, "/customers", owner.Tokens);
        Assert.Equal(1, list["totalCount"]!.GetValue<int>());
        var archived = await CallAsync(HttpMethod.Get, "/customers?isActive=false", owner.Tokens);
        Assert.Equal(catalog.CustomerId, Id(archived["items"]![0]!));
        await CallAsync(HttpMethod.Post, "/service-locations", owner.Tokens,
            new { customerId = catalog.CustomerId, name = "New", addressLine = "Address", city = "City", country = "TR" },
            HttpStatusCode.Conflict);
        await CallAsync(HttpMethod.Post, "/assets", owner.Tokens,
            new { customerId = catalog.CustomerId, serviceLocationId = catalog.LocationId, name = "Duplicate", assetCode = " gen-001 " },
            HttpStatusCode.Conflict);
    }
}
