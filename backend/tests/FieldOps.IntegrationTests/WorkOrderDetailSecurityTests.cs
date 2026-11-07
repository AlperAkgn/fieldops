using System.Net;
using FieldOps.Application.Common;
using FieldOps.Application.Tenancy;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Organizations;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FieldOps.IntegrationTests;

[Trait("Category", "Database")]
public sealed class WorkOrderDetailSecurityTests : CoreOperationsTestBase
{
    [Fact]
    public async Task ChecklistSubresourcesEnforceTenantOrderVersionAndImmutableDefinitions()
    {
        var owner = await RegisterAsync();
        var foreign = await RegisterAsync();
        var catalog = await CatalogAsync(owner.Tokens);
        var first = await OrderAsync(owner.Tokens, catalog);
        var second = await OrderAsync(owner.Tokens, catalog, "Second");
        var template = await CallAsync(HttpMethod.Post, "/checklist-templates", owner.Tokens,
            new { name = "Required inspection" }, HttpStatusCode.Created);
        var templatePath = $"/checklist-templates/{Id(template)}";
        template = await CallAsync(HttpMethod.Post, templatePath + "/items", owner.Tokens,
            new { expectedVersion = Version(template), label = "Check", position = 0, isRequired = true });
        var foreignTemplate = await CallAsync(HttpMethod.Post, "/checklist-templates", foreign.Tokens,
            new { name = "Foreign" }, HttpStatusCode.Created);
        var path = $"/work-orders/{Id(first)}/checklist";
        await CallAsync(HttpMethod.Post, path + "/apply", owner.Tokens,
            new { expectedVersion = 1, templateId = Id(foreignTemplate) }, HttpStatusCode.NotFound);
        await CallAsync(HttpMethod.Get, templatePath, foreign.Tokens, expected: HttpStatusCode.NotFound);
        var snapshot = await CallAsync(HttpMethod.Post, path + "/apply", owner.Tokens,
            new { expectedVersion = 1, templateId = Id(template) });
        var itemId = snapshot["items"]![0]!["id"]!.GetValue<Guid>();
        await CallAsync(HttpMethod.Post, path + "/apply", owner.Tokens,
            new { expectedVersion = 2, templateId = Id(template) }, HttpStatusCode.Conflict);
        await CallAsync(HttpMethod.Patch, path + $"/{itemId}", owner.Tokens,
            new { expectedVersion = 1, isCompleted = true }, HttpStatusCode.Conflict);
        await CallAsync(HttpMethod.Patch, $"/work-orders/{Id(second)}/checklist/{itemId}", owner.Tokens,
            new { expectedVersion = 1, isCompleted = true }, HttpStatusCode.NotFound);
        await CallAsync(HttpMethod.Patch, path + $"/{itemId}", owner.Tokens,
            new { expectedVersion = 2, isCompleted = true, label = "Spoof" }, HttpStatusCode.BadRequest);
        await CallAsync(HttpMethod.Post, $"/work-orders/{Id(first)}/comments", owner.Tokens,
            new { expectedVersion = 2, body = "Invalid\0text", visibility = "Internal" }, HttpStatusCode.BadRequest);
        await CallAsync(HttpMethod.Delete, templatePath + "?expectedVersion=2", owner.Tokens, expected: HttpStatusCode.NoContent);
        snapshot = await CallAsync(HttpMethod.Get, path, owner.Tokens);
        Assert.False(snapshot["items"]![0]!["isCompleted"]!.GetValue<bool>());
        Assert.Equal("Check", snapshot["items"]![0]!["label"]!.GetValue<string>());
        await CallAsync(HttpMethod.Post, $"/work-orders/{Id(second)}/checklist/apply", owner.Tokens,
            new { expectedVersion = 1, templateId = Id(template) }, HttpStatusCode.Conflict);
        var constraint = await Assert.ThrowsAsync<PostgresException>(() => InDatabaseAsync(context =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "WorkOrderChecklistItems" SET "SourceTemplateId" = {Id(foreignTemplate)} WHERE "Id" = {itemId}
                """)));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, constraint.SqlState);
    }

    [Fact]
    public async Task OperationalFiltersAndWriteGuardsApplyOutsideTheHttpEndpoints()
    {
        var owner = await RegisterAsync();
        var other = await RegisterAsync();
        await CatalogAsync(owner.Tokens);
        await CatalogAsync(other.Tokens);
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetValidated(
            new TenantActor(owner.Tokens.UserId, owner.Tokens.SessionId, owner.Tokens.TenantId, owner.Tokens.Role));
        var database = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
        Assert.Equal(1, await database.Customers.CountAsync());
        Assert.Equal(1, await database.ServiceLocations.CountAsync());
        Assert.Equal(1, await database.Assets.CountAsync());
        database.Customers.Add(Customer.Create(other.Tokens.TenantId,
            new CustomerDetails(CustomerType.Business, "Cross-tenant write"), DateTimeOffset.UtcNow));
        var failure = await Assert.ThrowsAsync<FieldOpsException>(() => database.SaveChangesAsync());
        Assert.Equal(ErrorKind.Forbidden, failure.Kind);
    }

    [Fact]
    public async Task InactiveAccountsCannotBeAssignedEvenIfMembershipWasLeftActive()
    {
        var owner = await RegisterAsync();
        var technician = await RegisterAsync();
        await AcceptMembershipAsync(owner, technician, OrganizationRole.Technician);
        var catalog = await CatalogAsync(owner.Tokens);
        var order = await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog));
        await InDatabaseAsync(async context =>
        {
            var account = await context.Users.SingleAsync(user => user.Id == technician.Tokens.UserId);
            account.IsActive = false;
            await context.SaveChangesAsync();
        });
        await CallAsync(HttpMethod.Post, $"/work-orders/{Id(order)}/assign", owner.Tokens,
            new
            {
                expectedVersion = Version(order),
                technicianUserIds = new[] { technician.Tokens.UserId },
                primaryTechnicianUserId = technician.Tokens.UserId
            }, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DowngradeCannotDeleteOperationalRowsAndAuditHistory()
    {
        var owner = await RegisterAsync();
        var catalog = await CatalogAsync(owner.Tokens);
        await OrderAsync(owner.Tokens, catalog);
        await InDatabaseAsync(async context =>
        {
            var previous = Assert.Single(context.Database.GetMigrations(),
                name => name.EndsWith("_RevokeUnactivatedAccountSessions", StringComparison.Ordinal));
            await Assert.ThrowsAsync<NotSupportedException>(() => context.GetService<IMigrator>().MigrateAsync(previous));
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.Equal(1, await context.WorkOrders.IgnoreQueryFilters().CountAsync());
            Assert.Equal(1, await context.Customers.IgnoreQueryFilters().CountAsync());
            Assert.True(await context.AuditLogs.IgnoreQueryFilters().CountAsync() >= 5);
            await context.Database.MigrateAsync();
        });
    }
}
