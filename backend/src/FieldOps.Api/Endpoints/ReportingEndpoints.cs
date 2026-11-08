using FieldOps.Application.Reporting;

namespace FieldOps.Api.Endpoints;

internal static class ReportingEndpoints
{
    public static void MapReportingEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/reports/work-orders", async ([AsParameters] ReportFilter filter, IReportingReader reader, CancellationToken ct) =>
            TypedResults.Ok(await reader.ReportAsync(filter, ct)))
            .WithTags("Reports").RequireAuthorization("OperationalReports").RequireRateLimiting("api").WithProblemResponses(400, 401, 403, 429);
        app.MapGet("/api/v1/technicians", async ([AsParameters] TechnicianFilter filter, IReportingReader reader, CancellationToken ct) =>
            TypedResults.Ok(await reader.TechniciansAsync(filter, ct)))
            .WithTags("Team").RequireAuthorization("OperationalReports").RequireRateLimiting("api").WithProblemResponses(400, 401, 403, 429);
    }
}
