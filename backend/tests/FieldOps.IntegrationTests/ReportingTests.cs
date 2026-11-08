using System.Net;
using FieldOps.Domain.Organizations;

namespace FieldOps.IntegrationTests;

public sealed class ReportingTests : CoreOperationsTestBase
{
    [Fact]
    public async Task Report_counters_workload_and_exclusive_created_range_match_committed_operations()
    {
        var owner = await RegisterAsync();
        var technician = await JoinAsync(owner, await RegisterAsync(), OrganizationRole.Technician);
        var catalog = await CatalogAsync(owner.Tokens);
        var first = await OrderAsync(owner.Tokens, catalog, "Overdue inspection");
        var overdue = await ScheduleAsync(owner.Tokens, first, "2020-01-01T10:00:00Z", "2020-01-01T11:00:00Z");
        await AssignAsync(owner.Tokens, overdue, technician.UserId);
        var completed = await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog, "Completed inspection"));
        completed = await AssignAsync(owner.Tokens, completed, technician.UserId);
        completed = await CallAsync(HttpMethod.Post, $"/work-orders/{Id(completed)}/start", owner.Tokens, new { expectedVersion = Version(completed) });
        await CallAsync(HttpMethod.Post, $"/work-orders/{Id(completed)}/complete", owner.Tokens, new { expectedVersion = Version(completed) });
        await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog, "Upcoming inspection"), "2090-01-01T10:00:00Z", "2090-01-01T11:00:00Z");
        var report = await CallAsync(HttpMethod.Get, "/reports/work-orders", owner.Tokens);
        Assert.Equal(3, report["totalOrders"]!.GetValue<int>());
        Assert.Equal(2, report["activeOrders"]!.GetValue<int>());
        Assert.Equal(1, report["completedOrders"]!.GetValue<int>());
        Assert.Equal(1, report["overdueOrders"]!.GetValue<int>());
        Assert.Equal(1, report["activeTechnicians"]!.GetValue<int>());
        Assert.Equal(33.3m, report["completionRate"]!.GetValue<decimal>());
        Assert.Equal(1, report["technicianWorkload"]![0]!["activeOrders"]!.GetValue<int>());
        Assert.Equal("Upcoming inspection", report["upcomingOrders"]![0]!["title"]!.GetValue<string>());
        var beforeFirst = await CallAsync(HttpMethod.Get, "/reports/work-orders?to=" + Uri.EscapeDataString(first["createdAt"]!.GetValue<string>()), owner.Tokens);
        Assert.Equal(0, beforeFirst["totalOrders"]!.GetValue<int>());
    }

    [Fact]
    public async Task Report_uses_real_rows_scoped_to_tenant_and_validates_dates()
    {
        var owner = await RegisterAsync();
        var other = await RegisterAsync();
        var catalog = await CatalogAsync(owner.Tokens);
        await OrderAsync(owner.Tokens, catalog);
        var report = await CallAsync(HttpMethod.Get, "/reports/work-orders", owner.Tokens);
        Assert.Equal(1, report["totalOrders"]!.GetValue<int>());
        Assert.Equal(1, report["activeOrders"]!.GetValue<int>());
        Assert.Equal(0, report["completedOrders"]!.GetValue<int>());
        Assert.Equal("Draft", report["statusDistribution"]![0]!["name"]!.GetValue<string>());
        Assert.NotEmpty(report["recentActivities"]!.AsArray());
        var empty = await CallAsync(HttpMethod.Get, "/reports/work-orders", other.Tokens);
        Assert.Equal(0, empty["totalOrders"]!.GetValue<int>());
        Assert.Empty(empty["recentActivities"]!.AsArray());
        var future = await CallAsync(HttpMethod.Get, "/reports/work-orders?from=2090-01-01T00:00:00Z", owner.Tokens);
        Assert.Equal(0, future["totalOrders"]!.GetValue<int>());
        await CallAsync(HttpMethod.Get, "/reports/work-orders?from=2090-01-02T00:00:00Z&to=2090-01-01T00:00:00Z", owner.Tokens, expected: HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Manager_can_find_active_technicians_without_owner_member_access()
    {
        var owner = await RegisterAsync();
        var manager = await JoinAsync(owner, await RegisterAsync(), OrganizationRole.Manager);
        var technician = await RegisterAsync();
        await JoinAsync(owner, technician, OrganizationRole.Technician);
        var outsider = await RegisterAsync();
        var result = await CallAsync(HttpMethod.Get, "/technicians?pageSize=1", manager);
        Assert.Equal(1, result["totalCount"]!.GetValue<int>());
        Assert.Equal(technician.Tokens.UserId, result["items"]![0]!["userId"]!.GetValue<Guid>());
        Assert.Null(result["items"]![0]!["email"]);
        var isolated = await CallAsync(HttpMethod.Get, "/technicians", outsider.Tokens);
        Assert.Equal(0, isolated["totalCount"]!.GetValue<int>());
        await CallAsync(HttpMethod.Get, "/technicians?pageSize=101", manager, expected: HttpStatusCode.BadRequest);
        await CallAsync(HttpMethod.Get, $"/organizations/{owner.Tokens.TenantId}/members", manager, expected: HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(OrganizationRole.Viewer)]
    [InlineData(OrganizationRole.Technician)]
    public async Task Reports_and_technician_directory_reject_non_administrators(OrganizationRole role)
    {
        var owner = await RegisterAsync();
        var member = await JoinAsync(owner, await RegisterAsync(), role);
        await CallAsync(HttpMethod.Get, "/reports/work-orders", member, expected: HttpStatusCode.Forbidden);
        await CallAsync(HttpMethod.Get, "/technicians", member, expected: HttpStatusCode.Forbidden);
        using var anonymous = await Client.GetAsync(Root + "/reports/work-orders");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
