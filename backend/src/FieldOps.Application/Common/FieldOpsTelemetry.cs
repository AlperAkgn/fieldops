using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FieldOps.Application.Common;

public static class FieldOpsTelemetry
{
    public const string Name = "FieldOps";
    public static readonly ActivitySource Activities = new(Name);
    private static readonly Meter Meter = new(Name);
    private static readonly Counter<long> Operations = Meter.CreateCounter<long>("fieldops.operations");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("fieldops.duration", "ms");
    private static readonly UpDownCounter<long> Connections = Meter.CreateUpDownCounter<long>("fieldops.signalr.connections");
    private static long pendingEvents;
    private static long pendingPush;

    static FieldOpsTelemetry()
    {
        Meter.CreateObservableGauge("fieldops.outbox.pending", () => Interlocked.Read(ref pendingEvents));
        Meter.CreateObservableGauge("fieldops.push.pending", () => Interlocked.Read(ref pendingPush));
    }

    // Callers supply only fixed operation and outcome names. Never attach IDs or payloads to metrics.
    public static void Record(string operation, string outcome, double? milliseconds = null)
    {
        var tags = new TagList { { "operation", operation }, { "outcome", outcome } };
        Operations.Add(1, tags);
        if (milliseconds is { } duration) Duration.Record(duration, tags);
    }

    public static void ConnectionChanged(int change) => Connections.Add(change);

    public static void ObservePending(long events, long push)
    {
        Interlocked.Exchange(ref pendingEvents, events);
        Interlocked.Exchange(ref pendingPush, push);
    }

    public static string? CurrentTraceParent => Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity
        ? activity.Id : null;

    public static Activity? StartDelivery(string? traceParent) =>
        ActivityContext.TryParse(traceParent, null, true, out var source)
            ? Activities.StartActivity("outbox.dispatch", ActivityKind.Consumer, default(ActivityContext), links: [new ActivityLink(source)])
            : Activities.StartActivity("outbox.dispatch", ActivityKind.Consumer);
}
