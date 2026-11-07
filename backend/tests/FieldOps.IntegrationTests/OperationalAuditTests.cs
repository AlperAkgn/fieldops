using System.Net;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.IntegrationTests;

[Trait("Category", "Database")]
public sealed class OperationalAuditTests : IdentityTestBase
{
    private const string Customers = "/api/v1/customers";
    private const string Audit = "/api/v1/audit-logs";

    [Fact]
    public async Task AuditIsTenantScopedFilterableAndLimitedToAdministrators()
    {
        var owner = await RegisterAsync();
        var outsider = await RegisterAsync();
        var viewer = await RegisterAsync();
        var viewerSession = await JoinAsync(owner, viewer, OrganizationRole.Viewer);
        using var first = await AuthorizedAsync(HttpMethod.Post, Customers, owner.Tokens,
            new { customerType = "Business", name = "First customer" });
        using var second = await AuthorizedAsync(HttpMethod.Post, Customers, outsider.Tokens,
            new { customerType = "Business", name = "Foreign customer" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var id = (await ReadJsonAsync(first))["id"]!.GetValue<Guid>();
        var foreignId = (await ReadJsonAsync(second))["id"]!.GetValue<Guid>();

        using var allowed = await AuthorizedAsync(HttpMethod.Get, $"{Audit}?resourceType=customer&action=created&pageSize=1", owner.Tokens);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var page = (await ReadJsonAsync(allowed)).AsObject();
        Assert.Equal(1, page["totalCount"]!.GetValue<int>());
        var log = Assert.Single(page["items"]!.AsArray())!;
        Assert.Equal(id, log["resourceId"]!.GetValue<Guid>());
        Assert.Equal(owner.Tokens.UserId, log["actorUserId"]!.GetValue<Guid>());
        Assert.Equal("created", log["action"]!.GetValue<string>());

        using var foreign = await AuthorizedAsync(HttpMethod.Get, $"{Audit}?resourceId={foreignId}", owner.Tokens);
        Assert.Equal(HttpStatusCode.OK, foreign.StatusCode);
        Assert.Empty((await ReadJsonAsync(foreign))["items"]!.AsArray());
        using var forbidden = await AuthorizedAsync(HttpMethod.Get, Audit, viewerSession);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var invalid = await AuthorizedAsync(HttpMethod.Get, $"{Audit}?pageSize=101", owner.Tokens);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task AuditInsertFailureRollsBackTheBusinessChange()
    {
        var owner = await RegisterAsync();
        await InDatabaseAsync(context => context.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "AuditLogs" ADD CONSTRAINT "Test_Reject_Audit" CHECK (false) NOT VALID"""));
        using var failed = await AuthorizedAsync(HttpMethod.Post, Customers, owner.Tokens,
            new { customerType = "Business", name = "Must roll back" });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.DoesNotContain("Test_Reject_Audit", await failed.Content.ReadAsStringAsync());
        using var customers = await AuthorizedAsync(HttpMethod.Get, Customers, owner.Tokens);
        Assert.Equal(HttpStatusCode.OK, customers.StatusCode);
        Assert.Empty((await ReadJsonAsync(customers))["items"]!.AsArray());
        Assert.Equal(0, await InDatabaseAsync(context => context.AuditLogs.IgnoreQueryFilters().CountAsync()));
    }

    [Fact]
    public async Task OperationalWriteRechecksRoleAfterWaitingForOrganizationLock()
    {
        var owner = await RegisterAsync();
        var manager = await RegisterAsync();
        var session = await JoinAsync(owner, manager, OrganizationRole.Manager);
        await using var connection = new NpgsqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var acquire = new NpgsqlCommand("""SELECT "Id" FROM "Organizations" WHERE "Id" = @tenant FOR UPDATE""",
                         connection, transaction))
        {
            acquire.Parameters.AddWithValue("tenant", session.TenantId);
            Assert.Equal(session.TenantId, await acquire.ExecuteScalarAsync());
        }

        var write = AuthorizedAsync(HttpMethod.Post, Customers, session, new { customerType = "Business", name = "Forbidden now" });
        await using (var observer = new NpgsqlConnection(DatabaseConnectionString))
        {
            await observer.OpenAsync();
            await using var waiting = new NpgsqlCommand("""
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database()
                    AND wait_event_type = 'Lock' AND query LIKE '%Organizations%' AND pid <> pg_backend_pid())
                """, observer);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            while ((bool)(await waiting.ExecuteScalarAsync(deadline.Token))! == false)
            {
                Assert.False(write.IsCompleted, "The request must wait for the held organization lock.");
                await Task.Delay(20, deadline.Token);
            }
        }

        await using (var demote = new NpgsqlCommand("""
                         UPDATE "OrganizationMemberships" SET "Role" = 'Viewer' WHERE "TenantId" = @tenant AND "UserId" = @actor
                         """, connection, transaction))
        {
            demote.Parameters.AddWithValue("tenant", session.TenantId);
            demote.Parameters.AddWithValue("actor", session.UserId);
            Assert.Equal(1, await demote.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
        using var result = await write;
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.Equal("membership_changed", (await ReadJsonAsync(result))["code"]!.GetValue<string>());
        Assert.Equal(0, await InDatabaseAsync(context => context.AuditLogs.IgnoreQueryFilters().CountAsync()));
    }
}
