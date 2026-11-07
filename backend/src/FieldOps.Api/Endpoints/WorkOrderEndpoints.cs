using FieldOps.Application.Operations;
using FieldOps.Application.WorkOrders;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Api.Endpoints;

public static class WorkOrderEndpoints
{
    public static void MapWorkOrderEndpoints(this WebApplication app)
    {
        var orders = app.MapGroup("/api/v1/work-orders").WithTags("Work Orders")
            .RequireAuthorization("OperationalRead").RequireRateLimiting("api").WithProblemResponses(401, 429);

        orders.MapGet("/", async ([AsParameters] WorkOrderListQuery query, IWorkOrderService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(query, ct))).WithProblemResponses(400);
        orders.MapGet("/{id:guid}", async (Guid id, IWorkOrderService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAsync(id, ct))).WithProblemResponses(404);
        orders.MapGet("/{id:guid}/history", async (Guid id, int? page, int? pageSize, IWorkOrderService service, CancellationToken ct) =>
            TypedResults.Ok(await service.HistoryAsync(id, new PageRequest(page ?? 1, pageSize ?? 25), ct))).WithProblemResponses(400, 404);

        orders.MapPost("/", async (CreateWorkOrderRequest request, IWorkOrderService service, CancellationToken ct) =>
        {
            var order = await service.CreateAsync(request, ct);
            return TypedResults.Created($"/api/v1/work-orders/{order.Id}", order);
        }).RequireAuthorization("OperationalWrite").WithProblemResponses(400, 403);
        orders.MapPatch("/{id:guid}", async (Guid id, UpdateWorkOrderRequest request, IWorkOrderService service, CancellationToken ct) =>
            TypedResults.Ok(await service.UpdateAsync(id, request, ct)))
            .RequireAuthorization("OperationalWrite").WithProblemResponses(400, 403, 404, 409);
        orders.MapPost("/{id:guid}/schedule", async (Guid id, ScheduleWorkOrderRequest request, IWorkOrderService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ScheduleAsync(id, request, ct)))
            .RequireAuthorization("OperationalWrite").WithProblemResponses(400, 403, 404, 409);
        orders.MapPost("/{id:guid}/assign", async (Guid id, AssignWorkOrderRequest request, IWorkOrderService service, CancellationToken ct) =>
            TypedResults.Ok(await service.AssignAsync(id, request, ct)))
            .RequireAuthorization("OperationalWrite").WithProblemResponses(400, 403, 404, 409);

        MapAction(orders, "start", WorkOrderAction.Start, "WorkOrderExecute");
        MapAction(orders, "pause", WorkOrderAction.Pause, "WorkOrderExecute");
        MapAction(orders, "resume", WorkOrderAction.Resume, "WorkOrderExecute");
        MapAction(orders, "complete", WorkOrderAction.Complete, "WorkOrderExecute");
        MapAction(orders, "cancel", WorkOrderAction.Cancel, "OperationalWrite");
    }

    private static void MapAction(RouteGroupBuilder group, string route, WorkOrderAction action, string policy)
    {
        group.MapPost($"/{{id:guid}}/{route}", async (Guid id, WorkOrderActionRequest request, IWorkOrderService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ExecuteAsync(id, action, request, ct)))
            .RequireAuthorization(policy).WithProblemResponses(400, 403, 404, 409);
    }
}
