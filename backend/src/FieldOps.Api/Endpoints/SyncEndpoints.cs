using FieldOps.Application.WorkOrders;

namespace FieldOps.Api.Endpoints;

public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this WebApplication app)
    {
        var sync = app.MapGroup("/api/v1/sync").WithTags("Technician Sync")
            .RequireAuthorization("WorkOrderExecute").RequireRateLimiting("api")
            .WithProblemResponses(400, 401, 403, 409, 429);
        sync.MapPost("/push", async (SyncPushRequest request, ISyncService service, CancellationToken ct) =>
            TypedResults.Ok(await service.PushAsync(request, ct)));
        sync.MapGet("/pull", async (int protocol, string? cursor, ISyncService service, CancellationToken ct) =>
            TypedResults.Ok(await service.PullAsync(protocol, cursor, ct)));
    }
}
