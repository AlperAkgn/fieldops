using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Domain.Organizations;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FieldOps.IntegrationTests;

[Trait("Category", "Database")]
public sealed class OfflineSyncTests : CoreOperationsTestBase
{
    [Fact]
    public async Task ConcurrentReplayWritesOneNoteAndChecksFingerprintAndAuthorization()
    {
        var owner = await RegisterAsync();
        var technician = await RegisterAsync();
        var other = await RegisterAsync();
        var actor = await JoinAsync(owner, technician, OrganizationRole.Technician);
        var replacement = await JoinAsync(owner, other, OrganizationRole.Technician);
        var order = await AssignAsync(owner.Tokens, await ScheduleAsync(owner.Tokens,
            await OrderAsync(owner.Tokens, await CatalogAsync(owner.Tokens))), actor.UserId);
        var path = $"/work-orders/{Id(order)}";
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        object Batch(string body) => new
        {
            protocol = 1,
            operations = new[] { new
        {
            id, orderId = Id(order), expectedVersion = Version(order), kind = "note", body, createdAt = now
        } }
        };

        var results = await Task.WhenAll(
            CallAsync(HttpMethod.Post, "/sync/push", actor, Batch("One offline note")),
            CallAsync(HttpMethod.Post, "/sync/push", actor, Batch("One offline note")));
        Assert.All(results, result => Assert.Equal(Version(order) + 1, First(result)["version"]!.GetValue<long>()));
        Assert.Single(results, result => First(result)["duplicate"]!.GetValue<bool>());
        Assert.Equal(1, (await CallAsync(HttpMethod.Get, path + "/comments", actor))["totalCount"]!.GetValue<int>());
        var changed = await CallAsync(HttpMethod.Post, "/sync/push", actor, Batch("Different note"));
        Assert.Equal("sync_operation_reused", First(changed)["code"]!.GetValue<string>());
        var audit = await CallAsync(HttpMethod.Get, $"/audit-logs?resourceId={Id(order)}", owner.Tokens);
        Assert.Single(audit["items"]!.AsArray(), row => row!["action"]!.GetValue<string>() == "comment_added");

        var before = await CallAsync(HttpMethod.Get, "/sync/pull?protocol=1", actor);
        Assert.Single(before["orders"]!.AsArray());
        var current = (await CallAsync(HttpMethod.Get, path, owner.Tokens))["order"]!;
        await AssignAsync(owner.Tokens, current, replacement.UserId);
        var denied = await CallAsync(HttpMethod.Post, "/sync/push", actor, Batch("One offline note"));
        Assert.Equal("work_order_not_found", First(denied)["code"]!.GetValue<string>());
        Assert.Null(First(denied)["version"]);
        var cursor = Uri.EscapeDataString(before["cursor"]!.GetValue<string>());
        await CallAsync(HttpMethod.Get, $"/sync/pull?protocol=1&cursor={cursor}", actor, expected: HttpStatusCode.Conflict);
        var empty = await CallAsync(HttpMethod.Get, "/sync/pull?protocol=1", actor);
        Assert.Empty(empty["orders"]!.AsArray());
        await CallAsync(HttpMethod.Get, $"/sync/pull?protocol=1&cursor={cursor}", replacement, expected: HttpStatusCode.Conflict);

        var foreignActor = await JoinAsync(await RegisterAsync(), await RegisterAsync(), OrganizationRole.Technician);
        await CallAsync(HttpMethod.Get, $"/sync/pull?protocol=1&cursor={cursor}", foreignActor, expected: HttpStatusCode.Conflict);
        var foreign = await CallAsync(HttpMethod.Post, "/sync/push", foreignActor, Batch("One offline note"));
        Assert.Equal("work_order_not_found", First(foreign)["code"]!.GetValue<string>());
        Assert.Null(First(foreign)["version"]);
    }

    [Fact]
    public async Task StaleAndExpiredOperationsDoNotApplyAndIndependentOrdersCanProceed()
    {
        var owner = await RegisterAsync();
        var technician = await RegisterAsync();
        var actor = await JoinAsync(owner, technician, OrganizationRole.Technician);
        var catalog = await CatalogAsync(owner.Tokens);
        var first = await AssignAsync(owner.Tokens, await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog)), actor.UserId);
        var second = await AssignAsync(owner.Tokens, await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog),
            "2027-01-10T11:00:00Z", "2027-01-10T12:00:00Z"), actor.UserId);
        var now = DateTimeOffset.UtcNow;
        object Action(JsonNode order, long version, DateTimeOffset at) => new
        { id = Guid.NewGuid(), orderId = Id(order), expectedVersion = version, kind = "start", createdAt = at };
        var result = await CallAsync(HttpMethod.Post, "/sync/push", actor, new
        {
            protocol = 1,
            operations = new[]
            {
                Action(first, Version(first) - 1, now), Action(first, Version(first), now),
                Action(second, Version(second), now), Action(first, Version(first), now.AddDays(-8))
            }
        });
        Assert.Equal(new[] { "conflict", "blocked", "applied", "blocked" },
            result["results"]!.AsArray().Select(row => row!["status"]!.GetValue<string>()));
        var expired = await CallAsync(HttpMethod.Post, "/sync/push", actor, new
        { protocol = 1, operations = new[] { Action(first, Version(first), now.AddDays(-8)) } });
        Assert.Equal("sync_operation_expired", First(expired)["code"]!.GetValue<string>());
        Assert.Equal("Assigned", (await CallAsync(HttpMethod.Get, $"/work-orders/{Id(first)}", actor))["order"]!["status"]!.GetValue<string>());
        await CallAsync(HttpMethod.Get, "/sync/pull?protocol=2", actor, expected: HttpStatusCode.BadRequest);
        await CallAsync(HttpMethod.Get, "/sync/pull?protocol=1", owner.Tokens, expected: HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PullNeverAdvancesPastAnUncommittedChange()
    {
        var owner = await RegisterAsync();
        var technician = await RegisterAsync();
        var actor = await JoinAsync(owner, technician, OrganizationRole.Technician);
        var catalog = await CatalogAsync(owner.Tokens);
        var first = await AssignAsync(owner.Tokens, await ScheduleAsync(owner.Tokens,
            await OrderAsync(owner.Tokens, catalog)), actor.UserId);
        var second = await AssignAsync(owner.Tokens, await ScheduleAsync(owner.Tokens,
            await OrderAsync(owner.Tokens, catalog), "2027-01-10T11:00:00Z", "2027-01-10T12:00:00Z"), actor.UserId);
        var baseline = await CallAsync(HttpMethod.Get, "/sync/pull?protocol=1", actor);
        await using var connection = new NpgsqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var update = new NpgsqlCommand("""
            UPDATE "WorkOrders" SET "Version" = "Version" + 1 WHERE "Id" = @order
            """, connection, transaction);
        update.Parameters.AddWithValue("order", Id(first));
        Assert.Equal(1, await update.ExecuteNonQueryAsync());

        var later = CallAsync(HttpMethod.Post, "/sync/push", actor, new
        {
            protocol = 1,
            operations = new[] { new
            {
                id = Guid.NewGuid(), orderId = Id(second), expectedVersion = Version(second),
                kind = "start", createdAt = DateTimeOffset.UtcNow
            } }
        });
        var whileOpen = await CallAsync(HttpMethod.Get, CursorPath(baseline), actor);
        Assert.Empty(whileOpen["orders"]!.AsArray());
        await transaction.CommitAsync();
        Assert.Equal("applied", First(await later)["status"]!.GetValue<string>());

        var changes = await CallAsync(HttpMethod.Get, CursorPath(whileOpen), actor);
        Assert.Equal(new[] { Id(first), Id(second) }, changes["orders"]!.AsArray().Select(row => Id(row!["order"]!)));
        Assert.Empty((await CallAsync(HttpMethod.Get, CursorPath(changes), actor))["orders"]!.AsArray());
    }

    [Fact]
    public async Task ReceiptFailureRollsBackOrderNoteAuditAndFeedBeforeSafeRetry()
    {
        var owner = await RegisterAsync();
        var technician = await RegisterAsync();
        var actor = await JoinAsync(owner, technician, OrganizationRole.Technician);
        var order = await AssignAsync(owner.Tokens, await ScheduleAsync(owner.Tokens,
            await OrderAsync(owner.Tokens, await CatalogAsync(owner.Tokens))), actor.UserId);
        var path = $"/work-orders/{Id(order)}";
        var baseline = await CallAsync(HttpMethod.Get, "/sync/pull?protocol=1", actor);
        var batch = new
        {
            protocol = 1,
            operations = new[] { new
        {
            id = Guid.NewGuid(), orderId = Id(order), expectedVersion = Version(order),
            kind = "note", body = "Retried only after rollback", createdAt = DateTimeOffset.UtcNow
        } }
        };
        await using var connection = new NpgsqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();
        await using var setup = new NpgsqlCommand("""
            CREATE FUNCTION refuse_sync_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'integration receipt failure'; END $$;
            CREATE TRIGGER refuse_sync_receipt BEFORE INSERT ON "SyncReceipts"
                 FOR EACH ROW EXECUTE FUNCTION refuse_sync_receipt();
            """, connection);
        await setup.ExecuteNonQueryAsync();

        await CallAsync(HttpMethod.Post, "/sync/push", actor, batch, HttpStatusCode.InternalServerError);
        var unchanged = await CallAsync(HttpMethod.Get, path, actor);
        Assert.Equal(Version(order), Version(unchanged["order"]!));
        Assert.Equal(0, (await CallAsync(HttpMethod.Get, path + "/comments", actor))["totalCount"]!.GetValue<int>());
        var audit = await CallAsync(HttpMethod.Get, $"/audit-logs?resourceId={Id(order)}", owner.Tokens);
        Assert.DoesNotContain(audit["items"]!.AsArray(), row => row!["action"]!.GetValue<string>() == "comment_added");
        Assert.Empty((await CallAsync(HttpMethod.Get, CursorPath(baseline), actor))["orders"]!.AsArray());

        await using var remove = new NpgsqlCommand("DROP TRIGGER refuse_sync_receipt ON \"SyncReceipts\"", connection);
        await remove.ExecuteNonQueryAsync();
        Assert.Equal("applied", First(await CallAsync(HttpMethod.Post, "/sync/push", actor, batch))["status"]!.GetValue<string>());
        Assert.Single((await CallAsync(HttpMethod.Get, CursorPath(baseline), actor))["orders"]!.AsArray());
        await CallAsync(HttpMethod.Post, "/auth/logout", actor, expected: HttpStatusCode.NoContent);
        await CallAsync(HttpMethod.Post, "/sync/push", actor, batch, HttpStatusCode.Unauthorized);
        await CallAsync(HttpMethod.Get, CursorPath(baseline), actor, expected: HttpStatusCode.Unauthorized);
    }

    private static string CursorPath(JsonNode response) =>
        "/sync/pull?protocol=1&cursor=" + Uri.EscapeDataString(response["cursor"]!.GetValue<string>());

    private static JsonNode First(JsonNode response) => response["results"]![0]!;

    [Fact]
    public async Task UpgradeBackfillsExistingOrdersWithoutChangingHistory()
    {
        await InDatabaseAsync(async context =>
        {
            var previous = context.Database.GetMigrations().Reverse().Skip(1).First();
            await context.GetService<IMigrator>().MigrateAsync(previous);
        });
        var owner = await RegisterAsync();
        var technician = await RegisterAsync();
        var actor = await JoinAsync(owner, technician, OrganizationRole.Technician);
        var order = await AssignAsync(owner.Tokens, await ScheduleAsync(owner.Tokens,
            await OrderAsync(owner.Tokens, await CatalogAsync(owner.Tokens))), actor.UserId);
        var path = $"/audit-logs?resourceId={Id(order)}";
        var audit = await CallAsync(HttpMethod.Get, path, owner.Tokens);
        await InDatabaseAsync(async context => await context.Database.MigrateAsync());
        var snapshot = await CallAsync(HttpMethod.Get, "/sync/pull?protocol=1", actor);
        var backfilled = Assert.Single(snapshot["orders"]!.AsArray())!["order"]!;
        Assert.Equal(Id(order), Id(backfilled));
        Assert.Equal(Version(order), Version(backfilled));
        Assert.Equal(audit["totalCount"]!.GetValue<int>(),
            (await CallAsync(HttpMethod.Get, path, owner.Tokens))["totalCount"]!.GetValue<int>());
    }
}
