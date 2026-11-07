using FieldOps.Application.Operations;
using FieldOps.Application.WorkOrderDetails;

namespace FieldOps.Api.Endpoints;

public static class WorkOrderDetailEndpoints
{
    public static void MapWorkOrderDetailEndpoints(this WebApplication app)
    {
        var templates = app.MapGroup("/api/v1/checklist-templates").WithTags("Checklist templates")
            .RequireAuthorization("OperationalWrite").RequireRateLimiting("api").WithProblemResponses(401, 403, 429);
        templates.MapGet("/", async (int? page, int? pageSize, string? search, bool? isActive,
            IChecklistTemplateService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(new PageRequest(page ?? 1, pageSize ?? 25), search, isActive, ct)))
            .WithProblemResponses(400);
        templates.MapGet("/{id:guid}", async (Guid id, IChecklistTemplateService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAsync(id, ct))).WithProblemResponses(404);
        templates.MapPost("/", async (CreateChecklistTemplateRequest request, IChecklistTemplateService service, CancellationToken ct) =>
        {
            var template = await service.CreateAsync(request, ct);
            return TypedResults.Created($"/api/v1/checklist-templates/{template.Id}", template);
        }).WithProblemResponses(400);
        templates.MapPatch("/{id:guid}", async (Guid id, UpdateChecklistTemplateRequest request, IChecklistTemplateService service,
            CancellationToken ct) => TypedResults.Ok(await service.UpdateAsync(id, request, ct))).WithProblemResponses(400, 404, 409);
        templates.MapDelete("/{id:guid}", async (Guid id, long expectedVersion, IChecklistTemplateService service, CancellationToken ct) =>
        {
            await service.ArchiveAsync(id, expectedVersion, ct);
            return TypedResults.NoContent();
        }).WithProblemResponses(400, 404, 409);
        templates.MapPost("/{id:guid}/items", async (Guid id, CreateChecklistTemplateItemRequest request, IChecklistTemplateService service,
            CancellationToken ct) => TypedResults.Ok(await service.AddItemAsync(id, request, ct))).WithProblemResponses(400, 404, 409);
        templates.MapPatch("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, UpdateChecklistTemplateItemRequest request,
            IChecklistTemplateService service, CancellationToken ct) => TypedResults.Ok(await service.UpdateItemAsync(id, itemId, request, ct)))
            .WithProblemResponses(400, 404, 409);
        templates.MapDelete("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, long expectedVersion,
            IChecklistTemplateService service, CancellationToken ct) => TypedResults.Ok(await service.ArchiveItemAsync(id, itemId, expectedVersion, ct)))
            .WithProblemResponses(400, 404, 409);

        var details = app.MapGroup("/api/v1/work-orders/{id:guid}").WithTags("Work orders")
            .RequireAuthorization("OperationalRead").RequireRateLimiting("api").WithProblemResponses(401, 404, 429);
        details.MapGet("/checklist", async (Guid id, IWorkOrderDetailService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetChecklistAsync(id, ct)));
        details.MapPost("/checklist/apply", async (Guid id, ApplyChecklistTemplateRequest request, IWorkOrderDetailService service,
            CancellationToken ct) => TypedResults.Ok(await service.ApplyChecklistAsync(id, request, ct)))
            .RequireAuthorization("OperationalWrite").WithProblemResponses(400, 403, 409);
        details.MapPatch("/checklist/{itemId:guid}", async (Guid id, Guid itemId, UpdateWorkOrderChecklistItemRequest request,
            IWorkOrderDetailService service, CancellationToken ct) => TypedResults.Ok(await service.UpdateChecklistItemAsync(id, itemId, request, ct)))
            .RequireAuthorization("WorkOrderExecute").WithProblemResponses(400, 403, 409);
        details.MapGet("/comments", async (Guid id, int? page, int? pageSize, IWorkOrderDetailService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListCommentsAsync(id, new PageRequest(page ?? 1, pageSize ?? 25), ct))).WithProblemResponses(400);
        details.MapPost("/comments", async (Guid id, AddWorkOrderCommentRequest request, IWorkOrderDetailService service, CancellationToken ct) =>
            TypedResults.Created((string?)null, await service.AddCommentAsync(id, request, ct)))
            .RequireAuthorization("WorkOrderExecute").WithProblemResponses(400, 403, 409);
    }
}
