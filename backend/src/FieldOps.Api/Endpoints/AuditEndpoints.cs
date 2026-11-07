using FieldOps.Application.Operations;

namespace FieldOps.Api.Endpoints;

internal static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app) =>
        app.MapGet("/api/v1/audit-logs", async ([AsParameters] AuditLogFilter filter, IAuditLogReader reader, CancellationToken ct) =>
            TypedResults.Ok(await reader.ListAsync(filter, ct)))
            .WithTags("Audit").RequireAuthorization("OperationalReports").RequireRateLimiting("api")
            .WithProblemResponses(400, 401, 403, 429);
}
