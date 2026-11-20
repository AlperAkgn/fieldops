using FieldOps.Infrastructure.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FieldOps.Api.Health;

internal static class ReadinessDeadline
{
    private static readonly object DeadlineKey = new();
    private static readonly TimeSpan ResponseBudget = TimeSpan.FromMilliseconds(4500);

    public static async Task StartAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments("/health/ready"))
        {
            await next(context);
            return;
        }

        // Include routing and a cold pipeline in the health probe's response budget.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        deadline.CancelAfter(ResponseBudget);
        context.Items[DeadlineKey] = deadline.Token;
        await next(context);
    }

    public static async Task WriteResponseAsync(HttpContext context, HealthCheckService healthChecks)
    {
        var deadline = (CancellationToken)context.Items[DeadlineKey]!;
        HealthReport report;
        try
        {
            deadline.ThrowIfCancellationRequested();
            report = await healthChecks.CheckHealthAsync(check => check.Tags.Contains("ready"), deadline).WaitAsync(deadline);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !context.RequestAborted.IsCancellationRequested)
        {
            // Respond while a cancelled dependency unwinds in its own health-check scope.
            report = new HealthReport(new Dictionary<string, HealthReportEntry>
            {
                [PostgreSqlReadinessCheck.Name] = new(HealthStatus.Unhealthy, null, TimeSpan.Zero, null, null)
            }, ResponseBudget);
        }
        await HealthResponseWriter.WriteAsync(context, report);
    }
}
