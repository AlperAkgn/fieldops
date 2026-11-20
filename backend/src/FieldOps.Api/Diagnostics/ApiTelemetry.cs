using System.Diagnostics;
using FieldOps.Application.Common;
using FieldOps.Application.Tenancy;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FieldOps.Api.Diagnostics;

internal static class ApiTelemetry
{
    public static void AddFieldOpsTelemetry(this WebApplicationBuilder builder)
    {
        var configured = builder.Configuration["Telemetry:OtlpEndpoint"];
        Uri? endpoint = null;
        if (!string.IsNullOrWhiteSpace(configured) &&
            (!Uri.TryCreate(configured, UriKind.Absolute, out endpoint) || endpoint.UserInfo.Length != 0 ||
                endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
                endpoint.Scheme != "https" && (!endpoint.IsLoopback || endpoint.Scheme != "http")))
            throw new InvalidOperationException("Configure an HTTPS OTLP origin or loopback HTTP for local diagnostics.");

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("FieldOps.Api")
                .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing =>
            {
                tracing.AddSource(FieldOpsTelemetry.Name);
                if (endpoint is not null) tracing.AddOtlpExporter(options => Configure(options, endpoint, "traces"));
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(FieldOpsTelemetry.Name);
                if (endpoint is not null) metrics.AddOtlpExporter(options => Configure(options, endpoint, "metrics"));
            });
    }

    private static void Configure(OtlpExporterOptions options, Uri endpoint, string signal)
    {
        options.Endpoint = new Uri(endpoint, $"v1/{signal}");
        options.Protocol = OtlpExportProtocol.HttpProtobuf;
        options.TimeoutMilliseconds = 3000;
    }

    public static async Task TrackRequestAsync(HttpContext context, RequestDelegate next)
    {
        var method = context.Request.Method switch
        {
            "GET" or "HEAD" or "POST" or "PUT" or "PATCH" or "DELETE" or "OPTIONS" or "CONNECT" => context.Request.Method,
            _ => "OTHER"
        };
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        var operation = route switch
        {
            string match when match.StartsWith("/api/v1/auth", StringComparison.Ordinal) => "auth",
            string match when match.StartsWith("/api/v1/sync", StringComparison.Ordinal) => "sync",
            string match when match.Contains("/attachments", StringComparison.Ordinal) => method == "POST" ? "attachment.upload" : "attachment.read",
            string match when match.StartsWith("/api/v1/work-orders", StringComparison.Ordinal) && method is "PATCH" or "POST" or "DELETE" => "work_order.write",
            _ => "http"
        };
        var started = Stopwatch.GetTimestamp();
        var parent = Activity.Current?.Context ?? default;
        if (parent == default && context.Request.Headers.TryGetValue("traceparent", out var traceParents) && traceParents.Count == 1)
            ActivityContext.TryParse(traceParents[0], null, true, out parent);
        using var activity = FieldOpsTelemetry.Activities.StartActivity(operation, ActivityKind.Server, parent);
        activity?.SetTag("http.request.method", method);
        if (route is not null) activity?.SetTag("http.route", route);
        var correlation = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        long? upgradedAt = null;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-ID"] = correlation;
            if (context.WebSockets.IsWebSocketRequest &&
                (context.Response.StatusCode == StatusCodes.Status101SwitchingProtocols ||
                    method == "CONNECT" && context.Response.StatusCode == StatusCodes.Status200OK))
            {
                upgradedAt = Stopwatch.GetTimestamp();
                activity?.DisplayName = "websocket.connection";
                FieldOpsTelemetry.Record("websocket.upgrade", "ok", Stopwatch.GetElapsedTime(started, upgradedAt.Value).TotalMilliseconds);
            }
            return Task.CompletedTask;
        });

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(FieldOpsTelemetry.Name);
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["Service"] = "FieldOps.Api",
            ["Environment"] = context.RequestServices.GetRequiredService<IWebHostEnvironment>().EnvironmentName,
            ["RequestId"] = context.TraceIdentifier,
            ["TraceId"] = correlation,
            ["CorrelationId"] = correlation
        });
        var outcome = "failed";
        try
        {
            await next(context);
            outcome = context.Response.StatusCode switch
            {
                401 => "unauthenticated",
                403 => "forbidden",
                409 => "conflict",
                429 => "rate_limited",
                >= 500 => "failed",
                >= 400 => "rejected",
                _ => "ok"
            };
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        finally
        {
            var duration = Stopwatch.GetElapsedTime(upgradedAt ?? started).TotalMilliseconds;
            var actor = context.RequestServices.GetService<TenantContext>()?.Actor;
            activity?.SetTag("http.response.status_code", context.Response.StatusCode);
            activity?.SetTag("fieldops.outcome", outcome);
            if (outcome == "failed") activity?.SetStatus(ActivityStatusCode.Error);
            if (upgradedAt.HasValue) operation = "websocket.connection";
            FieldOpsTelemetry.Record(operation, outcome, duration);
            if (!upgradedAt.HasValue && operation != "http") FieldOpsTelemetry.Record("http", outcome, duration);
            logger.LogInformation("{Operation} {Method} {Route} returned {StatusCode} in {DurationMs} ms with {Outcome} for {TenantId}",
                operation, method, route, context.Response.StatusCode, duration, outcome, actor?.TenantId);
        }
    }
}
