using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using FieldOps.Application.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace FieldOps.IntegrationTests;

public sealed class DeploymentSecurityTests
{
    [Theory]
    [InlineData("ConnectionStrings:Database", "Host=localhost;Database=fieldops;SSL Mode=Require", "VerifyFull")]
    [InlineData("AllowedHosts", "*", "AllowedHosts")]
    [InlineData("Cors:AllowedOrigins:0", "http://admin.example.test", "HTTPS required")]
    [InlineData("Email:UseStartTls", "false", "TLS SMTP")]
    [InlineData("ObjectStorage:CreateLocalBucket", "true", "private, provisioned")]
    [InlineData("Proxy:KnownProxies:0", "0.0.0.0", "exact proxy IP")]
    public void ProductionRejectsUnsafeConfiguration(string key, string value, string message)
    {
        using var factory = new ApiFactory("Production", settings: new Dictionary<string, string?> { [key] = value });
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains(message, error.Message);
    }

    [Theory]
    [InlineData("192.0.2.10", "https", 401)]
    [InlineData("192.0.2.11", "http", 400)]
    public async Task ProductionProcessesForwardedSchemeOnlyFromTrustedProxy(string peer, string scheme, int status)
    {
        await using var factory = new ApiFactory("Production", settings: new Dictionary<string, string?>
        {
            ["Proxy:KnownProxies:0"] = "192.0.2.10"
        });
        var context = await factory.Server.SendAsync(request =>
        {
            request.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            request.Request.Host = new HostString("localhost");
            request.Request.Method = "GET";
            request.Request.Scheme = "http";
            request.Request.Path = "/api/v1/auth/me";
            request.Request.Headers["X-Forwarded-Proto"] = "https";
            request.Request.Headers["X-Forwarded-For"] = "198.51.100.12";
        });
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal(scheme, context.Request.Scheme);
        Assert.Equal(scheme == "https" ? "198.51.100.12" : peer, context.Connection.RemoteIpAddress?.ToString());
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task PrivateTelemetryUsesRouteTemplateAndBoundedMetricLabels()
    {
        var traceId = ActivityTraceId.CreateRandom();
        var spans = new ConcurrentQueue<Activity>();
        using var traces = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FieldOpsTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = span => { if (span.TraceId == traceId) spans.Enqueue(span); }
        };
        ActivitySource.AddActivityListener(traces);
        var labels = new ConcurrentQueue<KeyValuePair<string, object?>[]>();
        using var metrics = new MeterListener();
        metrics.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == FieldOpsTelemetry.Name && instrument.Name == "fieldops.operations")
                listener.EnableMeasurementEvents(instrument);
        };
        metrics.SetMeasurementEventCallback<long>((_, _, tags, _) => labels.Enqueue(tags.ToArray()));
        metrics.Start();

        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me?ignored=private-query-marker");
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(traceId.ToString(), Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
        var span = Assert.Single(spans);
        Assert.Equal("auth", span.OperationName);
        Assert.Equal("/api/v1/auth/me", span.GetTagItem("http.route"));
        Assert.DoesNotContain("private-query-marker", span.TagObjects.Select(tag => tag.Value?.ToString()));
        Assert.Contains(labels, tags => tags.Length == 2 && tags[0].Key == "operation" && Equals(tags[0].Value, "auth") &&
            tags[1].Key == "outcome" && Equals(tags[1].Value, "unauthenticated"));
        using var scrape = await client.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.NotFound, scrape.StatusCode);
    }

    [Fact]
    public async Task OptionalOtlpExportsTracesAndMetricsWithoutSendingRequestValues()
    {
        await using var collector = await OtlpReceiver.StartAsync();

        await using var factory = new ApiFactory(settings: new Dictionary<string, string?>
        {
            ["Telemetry:OtlpEndpoint"] = collector.Origin
        });
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/auth/me?ignored=private-query-marker");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(factory.Services.GetRequiredService<TracerProvider>().ForceFlush(5000));
        Assert.True(factory.Services.GetRequiredService<MeterProvider>().ForceFlush(5000));

        Assert.Contains(collector.Payloads, received => received.Signal == "traces" &&
            received.Payload.Contains("/api/v1/auth/me", StringComparison.Ordinal));
        Assert.Contains(collector.Payloads, received => received.Signal == "metrics" &&
            received.Payload.Contains("fieldops.operations", StringComparison.Ordinal));
        Assert.All(collector.Payloads, received => Assert.DoesNotContain("private-query-marker", received.Payload));
    }
}
