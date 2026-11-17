using System.Data.Common;
using System.Diagnostics;
using FieldOps.Application.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FieldOps.Infrastructure.Persistence;

internal sealed class DatabaseTelemetry : DbCommandInterceptor
{
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Observe(eventData, "ok");
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default) => ValueTask.FromResult(ReaderExecuted(command, eventData, result));

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Observe(eventData, "ok");
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        int result, CancellationToken cancellationToken = default) => ValueTask.FromResult(NonQueryExecuted(command, eventData, result));

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Observe(eventData, "ok");
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        object? result, CancellationToken cancellationToken = default) => ValueTask.FromResult(ScalarExecuted(command, eventData, result));

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => Observe(eventData, "failed");

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        CommandFailed(command, eventData);
        return Task.CompletedTask;
    }

    private static void Observe(CommandEndEventData data, string outcome)
    {
        FieldOpsTelemetry.Record("postgresql.command", outcome, data.Duration.TotalMilliseconds);
        // Event data contains SQL and parameter metadata. Only emit the measured time and fixed labels.
        using var activity = FieldOpsTelemetry.Activities.StartActivity("postgresql.command", ActivityKind.Client,
            Activity.Current?.Context ?? default, startTime: data.StartTime);
        if (activity is null) return;
        activity.SetTag("db.system.name", "postgresql");
        activity.SetTag("fieldops.outcome", outcome);
        if (outcome == "failed") activity.SetStatus(ActivityStatusCode.Error);
        activity.SetEndTime((data.StartTime + data.Duration).UtcDateTime);
    }
}
