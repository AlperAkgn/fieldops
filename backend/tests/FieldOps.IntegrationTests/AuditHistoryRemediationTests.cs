using System.Text.Json.Nodes;
using System.Net;
using FieldOps.Application.Common;
using FieldOps.Application.Tenancy;
using FieldOps.Domain.Organizations;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FieldOps.IntegrationTests;

[Trait("Category", "Database")]
public sealed class AuditHistoryRemediationTests : CoreOperationsTestBase
{
    [Fact]
    public async Task RescheduleOverrideRetainsDatabaseBeforeAndAfterEvenWhenStatusDoesNotChange()
    {
        var owner = await RegisterAsync();
        var technician = await RegisterAsync();
        await AcceptMembershipAsync(owner, technician, OrganizationRole.Technician);
        var catalog = await CatalogAsync(owner.Tokens);
        var blocking = await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog, "Blocking"));
        await AssignAsync(owner.Tokens, blocking, technician.Tokens.UserId);
        var order = await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog),
            "2027-01-10T10:00:00Z", "2027-01-10T11:00:00Z");
        order = await AssignAsync(owner.Tokens, order, technician.Tokens.UserId);
        var previousVersion = Version(order);
        var historyCount = (await CallAsync(HttpMethod.Get, $"/work-orders/{Id(order)}/history", owner.Tokens))["totalCount"]!.GetValue<int>();
        var before = DateTimeOffset.UtcNow;
        order = await ScheduleAsync(owner.Tokens, order, "2027-01-10T09:30:00Z", "2027-01-10T10:30:00Z",
            "Emergency dispatch approved");
        var after = DateTimeOffset.UtcNow;
        Assert.Equal("Assigned", order["status"]!.GetValue<string>());
        Assert.Equal(historyCount, (await CallAsync(HttpMethod.Get,
            $"/work-orders/{Id(order)}/history", owner.Tokens))["totalCount"]!.GetValue<int>());

        var audit = await CallAsync(HttpMethod.Get, $"/audit-logs?resourceId={Id(order)}&action=rescheduled", owner.Tokens);
        var entry = Assert.Single(audit["items"]!.AsArray())!;
        Assert.NotNull(entry["metadata"]);
        var metadata = entry["metadata"]!;
        Assert.Equal(previousVersion, metadata["previousVersion"]!.GetValue<long>());
        Assert.Equal(Version(order), metadata["entityVersion"]!.GetValue<long>());
        AssertInstantChange(metadata, "plannedStartAt", "2027-01-10T10:00:00Z", "2027-01-10T09:30:00Z");
        AssertInstantChange(metadata, "plannedEndAt", "2027-01-10T11:00:00Z", "2027-01-10T10:30:00Z");
        Assert.Null(metadata["changes"]!["technicianUserIds"]);
        Assert.Equal(owner.Tokens.UserId, entry["actorUserId"]!.GetValue<Guid>());
        Assert.InRange(entry["occurredAt"]!.GetValue<DateTimeOffset>(), before.AddMilliseconds(-1), after);
        var overrides = await CallAsync(HttpMethod.Get,
            $"/audit-logs?resourceId={Id(order)}&action=availability_overridden", owner.Tokens);
        var overridden = Assert.Single(overrides["items"]!.AsArray())!;
        Assert.Equal("Emergency dispatch approved", overridden["reason"]!.GetValue<string>());
        Assert.Equal(metadata.ToJsonString(), overridden["metadata"]!.ToJsonString());
    }

    [Fact]
    public async Task AssignmentAndPrimaryChangesRetainPreviousAndPersistedTechnicianSets()
    {
        var owner = await RegisterAsync();
        var first = await RegisterAsync();
        var second = await RegisterAsync();
        var third = await RegisterAsync();
        foreach (var technician in new[] { first, second, third })
            await AcceptMembershipAsync(owner, technician, OrganizationRole.Technician);
        var catalog = await CatalogAsync(owner.Tokens);
        var order = await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog));
        var path = $"/work-orders/{Id(order)}/assign";
        order = await CallAsync(HttpMethod.Post, path, owner.Tokens, new
        {
            expectedVersion = Version(order),
            technicianUserIds = new[] { first.Tokens.UserId, second.Tokens.UserId },
            primaryTechnicianUserId = first.Tokens.UserId
        });
        var initial = await AssignmentMetadataAsync(Id(order), owner.Tokens, Version(order));
        Assert.Empty(initial["changes"]!["technicianUserIds"]!["previous"]!.AsArray());
        Assert.Null(initial["changes"]!["primaryTechnicianUserId"]!["previous"]);
        Assert.Equal(first.Tokens.UserId, initial["changes"]!["primaryTechnicianUserId"]!["current"]!.GetValue<Guid>());

        var previousVersion = Version(order);
        order = await CallAsync(HttpMethod.Post, path, owner.Tokens, new
        {
            expectedVersion = previousVersion,
            technicianUserIds = new[] { third.Tokens.UserId, second.Tokens.UserId },
            primaryTechnicianUserId = third.Tokens.UserId
        });
        var changed = await AssignmentMetadataAsync(Id(order), owner.Tokens, Version(order));
        Assert.Equal(previousVersion, changed["previousVersion"]!.GetValue<long>());
        var ids = changed["changes"]!["technicianUserIds"]!;
        Assert.Equal(new[] { first.Tokens.UserId, second.Tokens.UserId }.Order(),
            ids["previous"]!.AsArray().Select(node => node!.GetValue<Guid>()).Order());
        Assert.Equal(new[] { second.Tokens.UserId, third.Tokens.UserId }.Order(),
            ids["current"]!.AsArray().Select(node => node!.GetValue<Guid>()).Order());
        var primary = changed["changes"]!["primaryTechnicianUserId"]!;
        Assert.Equal(first.Tokens.UserId, primary["previous"]!.GetValue<Guid>());
        Assert.Equal(third.Tokens.UserId, primary["current"]!.GetValue<Guid>());
        Assert.Null(changed["changes"]!["plannedStartAt"]);

        order = await CallAsync(HttpMethod.Post, path, owner.Tokens, new
        {
            expectedVersion = Version(order),
            technicianUserIds = new[] { second.Tokens.UserId, third.Tokens.UserId },
            primaryTechnicianUserId = second.Tokens.UserId
        });
        var primaryOnly = await AssignmentMetadataAsync(Id(order), owner.Tokens, Version(order));
        Assert.Null(primaryOnly["changes"]!["technicianUserIds"]);
        Assert.Equal(third.Tokens.UserId, primaryOnly["changes"]!["primaryTechnicianUserId"]!["previous"]!.GetValue<Guid>());
        Assert.Equal(second.Tokens.UserId, primaryOnly["changes"]!["primaryTechnicianUserId"]!["current"]!.GetValue<Guid>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOverrideAuditRollsBackSchedulingOrAssignmentAndAllAuditRows(bool changeAssignment)
    {
        var owner = await RegisterAsync();
        var first = await RegisterAsync();
        var second = await RegisterAsync();
        await AcceptMembershipAsync(owner, first, OrganizationRole.Technician);
        await AcceptMembershipAsync(owner, second, OrganizationRole.Technician);
        var catalog = await CatalogAsync(owner.Tokens);
        var blocking = await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog, "Blocking"));
        await AssignAsync(owner.Tokens, blocking, changeAssignment ? second.Tokens.UserId : first.Tokens.UserId);
        var start = changeAssignment ? "2027-01-10T09:00:00Z" : "2027-01-10T10:00:00Z";
        var end = changeAssignment ? "2027-01-10T10:00:00Z" : "2027-01-10T11:00:00Z";
        var order = await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog), start, end);
        order = await AssignAsync(owner.Tokens, order, first.Tokens.UserId);
        var orderId = Id(order);
        var auditIds = await InDatabaseAsync(context => context.AuditLogs.IgnoreQueryFilters()
            .Where(log => log.ResourceId == orderId).OrderBy(log => log.Id).Select(log => log.Id).ToArrayAsync());
        var assignmentIds = await InDatabaseAsync(context => context.WorkOrderAssignments.IgnoreQueryFilters()
            .Where(item => item.WorkOrderId == orderId && item.RemovedAt == null).Select(item => item.Id).ToArrayAsync());
        await InDatabaseAsync(context => context.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "AuditLogs" ADD CONSTRAINT "Test_Reject_Override_Audit"
            CHECK ("Action" <> 'availability_overridden') NOT VALID
            """));
        if (changeAssignment)
            await CallAsync(HttpMethod.Post, $"/work-orders/{orderId}/assign", owner.Tokens,
                new
                {
                    expectedVersion = Version(order),
                    technicianUserIds = new[] { second.Tokens.UserId },
                    primaryTechnicianUserId = second.Tokens.UserId,
                    overrideReason = "Approved reassignment"
                },
                HttpStatusCode.InternalServerError);
        else
            await CallAsync(HttpMethod.Post, $"/work-orders/{orderId}/schedule", owner.Tokens,
                new
                {
                    expectedVersion = Version(order),
                    plannedStartAt = "2027-01-10T09:30:00Z",
                    plannedEndAt = "2027-01-10T10:30:00Z",
                    overrideReason = "Approved reschedule"
                },
                HttpStatusCode.InternalServerError);

        var detail = await CallAsync(HttpMethod.Get, $"/work-orders/{orderId}", owner.Tokens);
        Assert.Equal(Version(order), Version(detail["order"]!));
        Assert.Equal(DateTimeOffset.Parse(start), detail["order"]!["plannedStartAt"]!.GetValue<DateTimeOffset>());
        Assert.Equal(first.Tokens.UserId, Assert.Single(detail["assignments"]!.AsArray())!["technicianUserId"]!.GetValue<Guid>());
        Assert.Equal(auditIds, await InDatabaseAsync(context => context.AuditLogs.IgnoreQueryFilters()
            .Where(log => log.ResourceId == orderId).OrderBy(log => log.Id).Select(log => log.Id).ToArrayAsync()));
        Assert.Equal(assignmentIds, await InDatabaseAsync(context => context.WorkOrderAssignments.IgnoreQueryFilters()
            .Where(item => item.WorkOrderId == orderId && item.RemovedAt == null).Select(item => item.Id).ToArrayAsync()));
    }

    [Fact]
    public async Task StaleOrForgedRequestsLeaveOnlyTheSuccessfulAuditAndOtherTenantsCannotReadIt()
    {
        var owner = await RegisterAsync();
        var other = await RegisterAsync();
        var catalog = await CatalogAsync(owner.Tokens);
        var order = await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog));
        var staleVersion = Version(order);
        order = await ScheduleAsync(owner.Tokens, order, "2027-01-10T10:00:00Z", "2027-01-10T11:00:00Z");
        var path = $"/work-orders/{Id(order)}/schedule";
        await CallAsync(HttpMethod.Post, path, owner.Tokens, new
        {
            expectedVersion = staleVersion,
            plannedStartAt = "2027-01-10T11:00:00Z",
            plannedEndAt = "2027-01-10T12:00:00Z"
        }, HttpStatusCode.Conflict);
        await CallAsync(HttpMethod.Post, path, owner.Tokens, new
        {
            expectedVersion = Version(order),
            plannedStartAt = "2027-01-10T11:00:00Z",
            plannedEndAt = "2027-01-10T12:00:00Z",
            previousPlannedStartAt = "1999-01-01T00:00:00Z"
        }, HttpStatusCode.BadRequest);
        var audit = await CallAsync(HttpMethod.Get, $"/audit-logs?resourceId={Id(order)}&action=rescheduled", owner.Tokens);
        var entry = Assert.Single(audit["items"]!.AsArray())!;
        Assert.Equal(Version(order), entry["metadata"]!["entityVersion"]!.GetValue<long>());
        var foreign = await CallAsync(HttpMethod.Get, $"/audit-logs?resourceId={Id(order)}", other.Tokens);
        Assert.Empty(foreign["items"]!.AsArray());
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetValidated(
            new TenantActor(other.Tokens.UserId, other.Tokens.SessionId, other.Tokens.TenantId, other.Tokens.Role));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>().AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task RecordedAuditCannotBeUpdatedDeletedOrTruncatedThroughEfOrSql()
    {
        var owner = await RegisterAsync();
        var catalog = await CatalogAsync(owner.Tokens);
        var order = await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog));
        foreach (var delete in new[] { false, true })
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetValidated(
                new TenantActor(owner.Tokens.UserId, owner.Tokens.SessionId, owner.Tokens.TenantId, owner.Tokens.Role));
            var database = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
            var log = await database.AuditLogs.FirstAsync();
            if (delete) database.AuditLogs.Remove(log);
            else database.Entry(log).Property(item => item.Reason).CurrentValue = "Tampered";
            var error = await Assert.ThrowsAsync<FieldOpsException>(() => database.SaveChangesAsync());
            Assert.Equal("audit_immutable", error.Code);
        }
        foreach (var sql in new[]
        {
            """UPDATE "AuditLogs" SET "Reason" = 'Tampered'""",
            """DELETE FROM "AuditLogs" """,
            """TRUNCATE TABLE "AuditLogs" """
        })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                InDatabaseAsync(context => context.Database.ExecuteSqlRawAsync(sql)));
            Assert.Equal("55000", error.SqlState);
        }
        var result = await CallAsync(HttpMethod.Get, $"/audit-logs?resourceId={Id(order)}&action=scheduled", owner.Tokens);
        Assert.NotNull(Assert.Single(result["items"]!.AsArray())!["metadata"]);
    }

    private async Task<JsonNode> AssignmentMetadataAsync(Guid orderId, FieldOps.Application.Identity.TokenPair tokens, long version)
    {
        var audit = await CallAsync(HttpMethod.Get, $"/audit-logs?resourceId={orderId}&action=assignments_changed", tokens);
        return Assert.Single(audit["items"]!.AsArray(),
            item => item!["metadata"]!["entityVersion"]!.GetValue<long>() == version)!["metadata"]!;
    }

    private static void AssertInstantChange(JsonNode metadata, string field, string previous, string current)
    {
        var change = metadata["changes"]![field]!;
        Assert.Equal(DateTimeOffset.Parse(previous), change["previous"]!.GetValue<DateTimeOffset>());
        Assert.Equal(DateTimeOffset.Parse(current), change["current"]!.GetValue<DateTimeOffset>());
    }
}
