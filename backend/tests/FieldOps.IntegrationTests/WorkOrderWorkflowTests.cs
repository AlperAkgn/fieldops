using System.Net;
using FieldOps.Domain.Organizations;

namespace FieldOps.IntegrationTests;

[Trait("Category", "Database")]
public sealed class WorkOrderWorkflowTests : CoreOperationsTestBase
{
    [Fact]
    public async Task ManagerAndAssignedTechnicianCompleteTheRealWorkflowWithImmutableChecklistAndAudit()
    {
        var owner = await RegisterAsync();
        var manager = await RegisterAsync();
        var technician = await RegisterAsync();
        var viewer = await RegisterAsync();
        var management = await JoinAsync(owner, manager, OrganizationRole.Manager);
        var execution = await JoinAsync(owner, technician, OrganizationRole.Technician);
        var readOnly = await JoinAsync(owner, viewer, OrganizationRole.Viewer);
        var catalog = await CatalogAsync(management);
        var order = await OrderAsync(management, catalog);
        var path = $"/work-orders/{Id(order)}";
        var template = await CallAsync(HttpMethod.Post, "/checklist-templates", management,
            new { name = "Safety inspection" }, HttpStatusCode.Created);
        var templatePath = $"/checklist-templates/{Id(template)}";
        template = await CallAsync(HttpMethod.Post, templatePath + "/items", management,
            new { label = "Confirm pressure", position = 0, isRequired = true, expectedVersion = Version(template) });
        var sourceId = template["items"]![0]!["id"]!.GetValue<Guid>();
        var checklist = await CallAsync(HttpMethod.Post, path + "/checklist/apply", management,
            new { templateId = Id(template), expectedVersion = Version(order) });
        var itemId = checklist["items"]![0]!["id"]!.GetValue<Guid>();
        var appliedVersion = checklist["workOrderVersion"]!.GetValue<long>();
        await CallAsync(HttpMethod.Patch, templatePath + $"/items/{sourceId}", management,
            new { label = "Changed later", isRequired = false, expectedVersion = Version(template) });
        checklist = await CallAsync(HttpMethod.Get, path + "/checklist", management);
        Assert.Equal("Confirm pressure", checklist["items"]![0]!["label"]!.GetValue<string>());
        Assert.True(checklist["items"]![0]!["isRequired"]!.GetValue<bool>());

        await CallAsync(HttpMethod.Post, path + "/comments", management,
            new { body = "Management-only note", visibility = "Internal", expectedVersion = appliedVersion }, HttpStatusCode.Created);
        order = (await CallAsync(HttpMethod.Get, path, management))["order"]!;
        order = await ScheduleAsync(management, order);
        order = await AssignAsync(management, order, technician.Tokens.UserId);
        await CallAsync(HttpMethod.Get, $"/assets/{catalog.AssetId}", execution, expected: HttpStatusCode.Forbidden);
        var detail = await CallAsync(HttpMethod.Get, path, execution);
        Assert.Equal("1 Service Street", detail["serviceLocation"]!["addressLine"]!.GetValue<string>());
        Assert.DoesNotContain("Private catalog notes", detail.ToJsonString());
        var comments = await CallAsync(HttpMethod.Get, path + "/comments", execution);
        Assert.Empty(comments["items"]!.AsArray());
        var viewerComments = await CallAsync(HttpMethod.Get, path + "/comments", readOnly);
        Assert.Empty(viewerComments["items"]!.AsArray());
        await CallAsync(HttpMethod.Post, path + "/comments", execution,
            new { body = "Forbidden internal", visibility = "Internal", expectedVersion = Version(order) }, HttpStatusCode.Forbidden);
        await CallAsync(HttpMethod.Post, path + "/start", readOnly,
            new { expectedVersion = Version(order) }, HttpStatusCode.Forbidden);
        order = await CallAsync(HttpMethod.Post, path + "/start", execution, new { expectedVersion = Version(order) });
        order = await CallAsync(HttpMethod.Post, path + "/pause", execution, new { expectedVersion = Version(order) });
        order = await CallAsync(HttpMethod.Post, path + "/resume", execution, new { expectedVersion = Version(order) });
        var denied = await CallAsync(HttpMethod.Post, path + "/complete", execution,
            new { expectedVersion = Version(order) }, HttpStatusCode.Conflict);
        Assert.Equal("required_checklist_incomplete", denied["code"]!.GetValue<string>());
        checklist = await CallAsync(HttpMethod.Patch, path + $"/checklist/{itemId}", execution,
            new { isCompleted = true, expectedVersion = Version(order) });
        Assert.Equal(100, checklist["progressPercent"]!.GetValue<int>());
        var comment = await CallAsync(HttpMethod.Post, path + "/comments", execution,
            new
            {
                body = "Checked pressure",
                visibility = "TechnicianVisible",
                expectedVersion = checklist["workOrderVersion"]!.GetValue<long>()
            }, HttpStatusCode.Created);
        order = await CallAsync(HttpMethod.Post, path + "/complete", execution,
            new { expectedVersion = comment["workOrderVersion"]!.GetValue<long>() });
        Assert.Equal("Completed", order["status"]!.GetValue<string>());
        Assert.NotNull(order["actualEndAt"]);
        await CallAsync(HttpMethod.Post, path + "/cancel", management,
            new { expectedVersion = Version(order) }, HttpStatusCode.Conflict);
        await CallAsync(HttpMethod.Post, path + "/comments", execution,
            new { body = "Too late", visibility = "TechnicianVisible", expectedVersion = Version(order) }, HttpStatusCode.Conflict);
        var history = await CallAsync(HttpMethod.Get, path + "/history", execution);
        Assert.Equal(7, history["totalCount"]!.GetValue<int>());
        var serviceHistory = await CallAsync(HttpMethod.Get, $"/assets/{catalog.AssetId}/service-history", management);
        Assert.Equal(Id(order), serviceHistory["items"]![0]!["workOrderId"]!.GetValue<Guid>());
        await CallAsync(HttpMethod.Delete, $"/customers/{catalog.CustomerId}?expectedVersion=1", management, expected: HttpStatusCode.NoContent);
        var preserved = await CallAsync(HttpMethod.Get, path, management);
        Assert.Equal("Customer", preserved["customer"]!["name"]!.GetValue<string>());
        await CallAsync(HttpMethod.Get, $"/assets/{catalog.AssetId}/service-history", management);
        var audit = await CallAsync(HttpMethod.Get, $"/audit-logs?resourceId={Id(order)}", management);
        Assert.True(audit["totalCount"]!.GetValue<int>() >= 10);
    }
}
