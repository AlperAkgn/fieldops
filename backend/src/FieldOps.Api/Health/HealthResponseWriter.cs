using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FieldOps.Api.Health;

public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        // Deliberately exclude descriptions, exceptions, connection details, and check data.
        var response = new HealthResponse(report.Status.ToString(), report.Entries
            .Select(entry => new HealthCheckResponse(entry.Key, entry.Value.Status.ToString())).ToArray());
        context.Response.StatusCode = report.Status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;
        context.Response.Headers.CacheControl = "no-store, no-cache";
        context.Response.Headers.Pragma = "no-cache";

        return context.Response.WriteAsJsonAsync(response, context.RequestAborted);
    }
}
