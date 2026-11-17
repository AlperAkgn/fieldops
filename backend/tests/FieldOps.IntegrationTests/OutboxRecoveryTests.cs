using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FieldOps.Application.Collaboration;
using FieldOps.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace FieldOps.IntegrationTests;

[Trait("Category", "Database")]
public sealed class OutboxRecoveryTests : CoreOperationsTestBase
{
    private readonly InterruptedDelivery delivery = new();
    private readonly TestClock clock = new();

    protected override void ConfigureCollaborators(IServiceCollection services)
    {
        services.RemoveAll<IOrderChangeDelivery>();
        services.AddSingleton<IOrderChangeDelivery>(delivery);
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(clock);
    }

    [Fact]
    public async Task DispatchUsesCompletionTimeForNewlyCommittedEventsAndSlowFailures()
    {
        var owner = await RegisterAsync();
        var otherOwner = await RegisterAsync();
        var catalog = await CatalogAsync(owner.Tokens);
        var otherCatalog = await CatalogAsync(otherOwner.Tokens);
        var initial = Id(await OrderAsync(owner.Tokens, catalog));
        clock.Advance(TimeSpan.FromMilliseconds(100));
        var failed = Id(await OrderAsync(owner.Tokens, catalog));
        var late = Guid.Empty;
        DateTimeOffset firstCompleted = default, failureInstant = default, lateCompleted = default;
        delivery.OnDelivery = async change =>
        {
            if (change.WorkOrderId == initial)
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                late = Id(await OrderAsync(otherOwner.Tokens, otherCatalog));
                firstCompleted = clock.GetUtcNow();
            }
            else if (change.WorkOrderId == failed)
            {
                clock.Advance(TimeSpan.FromSeconds(6));
                failureInstant = clock.GetUtcNow();
                throw new TimeoutException();
            }
            else if (change.WorkOrderId == late)
            {
                clock.Advance(TimeSpan.FromSeconds(2));
                lateCompleted = clock.GetUtcNow();
            }
        };
        await DispatchAsync();
        await using var connection = new NpgsqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();
        await using var query = new NpgsqlCommand("""
            SELECT "WorkOrderId", "CreatedAt", "DispatchedAt", "NextAttemptAt" FROM "WorkOrderEvents"
            """, connection);
        await using var rows = await query.ExecuteReaderAsync();
        var checkedEvents = 0;
        while (await rows.ReadAsync())
        {
            var order = rows.GetGuid(0);
            if (order == initial || order == late)
            {
                Assert.True(rows.GetFieldValue<DateTimeOffset>(2) >= rows.GetFieldValue<DateTimeOffset>(1));
                Assert.Equal(order == initial ? firstCompleted : lateCompleted, rows.GetFieldValue<DateTimeOffset>(2));
            }
            else if (order == failed)
            {
                Assert.True(rows.IsDBNull(2));
                Assert.Equal(failureInstant.AddSeconds(5), rows.GetFieldValue<DateTimeOffset>(3));
            }
            else continue;
            checkedEvents++;
        }
        Assert.Equal(3, checkedEvents);
    }

    [Fact]
    public async Task TerminalFailureKeepsChronologicalStateThroughBackwardClockAdjustment()
    {
        var owner = await RegisterAsync();
        var orderId = Id(await OrderAsync(owner.Tokens, await CatalogAsync(owner.Tokens)));
        var created = clock.GetUtcNow();
        await InDatabaseAsync(async context => await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "WorkOrderEvents" SET "Attempts" = 7 WHERE "WorkOrderId" = {orderId}
            """));
        delivery.OnDelivery = _ =>
        {
            clock.Advance(TimeSpan.FromSeconds(-1));
            return Task.FromException(new TimeoutException());
        };
        await DispatchAsync();
        clock.Advance(TimeSpan.FromHours(1));
        await DispatchAsync();
        Assert.Single(delivery.Calls, change => change.WorkOrderId == orderId);
        await InDatabaseAsync(async context => Assert.True(await context.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM "WorkOrderEvents" WHERE "WorkOrderId" = {orderId}
                AND "Attempts" = 8 AND "FailedAt" = {created} AND "FailedAt" >= "CreatedAt"
                AND "DispatchedAt" IS NULL) AS "Value"
            """).SingleAsync()));
    }

    [Fact]
    public async Task DowngradeRefusesToLoseTerminalStateAndLeavesMigrationAndRowsIntact()
    {
        var owner = await RegisterAsync();
        var orderId = Id(await OrderAsync(owner.Tokens, await CatalogAsync(owner.Tokens)));
        Guid eventId = default;
        var terminalAt = clock.GetUtcNow();
        await InDatabaseAsync(async context =>
        {
            var previous = context.Database.GetMigrations().Single(name => name.EndsWith("_CollaborationAndAttachments", StringComparison.Ordinal));
            var migrator = context.GetService<IMigrator>();
            var auditCount = await context.AuditLogs.IgnoreQueryFilters().CountAsync();
            await migrator.MigrateAsync(previous);
            await context.Database.MigrateAsync();
            var applied = await context.Database.GetAppliedMigrationsAsync();
            eventId = await context.Database.SqlQuery<Guid>($"""
                SELECT "Id" AS "Value" FROM "WorkOrderEvents" WHERE "WorkOrderId" = {orderId}
                """).SingleAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "WorkOrderEvents" SET "Attempts" = 8, "FailedAt" = {terminalAt} WHERE "Id" = {eventId}
                """);
            var refused = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
            Assert.Contains("delivery state", refused.MessageText);
            await context.Database.MigrateAsync();
            Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
            Assert.Equal(auditCount, await context.AuditLogs.IgnoreQueryFilters().CountAsync());
            Assert.True(await context.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT 1 FROM "WorkOrderEvents" WHERE "Id" = {eventId} AND "WorkOrderId" = {orderId}
                    AND "Attempts" = 8 AND "FailedAt" = {terminalAt} AND "DispatchedAt" IS NULL) AS "Value"
                """).SingleAsync());
        });
        await DispatchAsync();
        Assert.DoesNotContain(delivery.Calls, call => call.EventId == eventId);
    }

    [Fact]
    public async Task FailedDeliveryBacksOffAndRestartsWithoutBlockingAnotherOrderOrChangingEventIdentity()
    {
        var owner = await RegisterAsync();
        var catalog = await CatalogAsync(owner.Tokens);
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + "/work-orders")
        {
            Content = JsonContent.Create(new { catalog.CustomerId, ServiceLocationId = catalog.LocationId, Title = "Interrupted delivery", Priority = "High" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Tokens.AccessToken);
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        using var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var affected = Id(await ReadJsonAsync(response));
        var other = Id(await OrderAsync(owner.Tokens, catalog));
        delivery.Unavailable = affected;

        var linked = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FieldOpsTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = span => { if (span.Links.Any(link => link.Context.TraceId == traceId)) linked.Enqueue(span); }
        };
        ActivitySource.AddActivityListener(listener);

        await DispatchAsync();
        Assert.Single(delivery.Calls, call => call.WorkOrderId == other);
        var first = Assert.Single(delivery.Calls, call => call.WorkOrderId == affected);
        Assert.Single(linked);
        await using var connection = new NpgsqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();
        await using var attempts = new NpgsqlCommand("""
            SELECT "Attempts", "NextAttemptAt" > "CreatedAt", "DispatchedAt", "FailedAt"
            FROM "WorkOrderEvents" WHERE "Id" = @event
            """, connection);
        attempts.Parameters.AddWithValue("event", first.EventId);
        await using (var row = await attempts.ExecuteReaderAsync())
        {
            Assert.True(await row.ReadAsync());
            Assert.Equal(1, row.GetInt32(0));
            Assert.True(row.GetBoolean(1));
            Assert.True(row.IsDBNull(2));
            Assert.True(row.IsDBNull(3));
        }
        delivery.Unavailable = null;
        await DispatchAsync();
        Assert.Single(delivery.Calls, change => change.WorkOrderId == affected);
        clock.Advance(TimeSpan.FromSeconds(5));
        await DispatchAsync();
        await DispatchAsync();
        Assert.Equal(new[] { first.EventId, first.EventId }, delivery.Calls.Where(call => call.WorkOrderId == affected).Select(call => call.EventId));
        await using var final = await attempts.ExecuteReaderAsync();
        Assert.True(await final.ReadAsync());
        Assert.False(final.IsDBNull(2));
        Assert.True(final.IsDBNull(3));
    }

    private async Task DispatchAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICollaborationDispatcher>().DispatchAsync(CancellationToken.None);
    }

    private sealed class InterruptedDelivery : IOrderChangeDelivery
    {
        public Guid? Unavailable { get; set; }
        public Func<OrderChanged, Task>? OnDelivery { get; set; }
        public List<OrderChanged> Calls { get; } = [];

        public Task DeliverAsync(Guid tenantId, OrderChanged change, IReadOnlyCollection<Guid> formerAssignees, CancellationToken cancellationToken)
        {
            Calls.Add(change);
            if (change.WorkOrderId == Unavailable) return Task.FromException(new TimeoutException());
            return OnDelivery?.Invoke(change) ?? Task.CompletedTask;
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan span) => now += span;
    }
}
