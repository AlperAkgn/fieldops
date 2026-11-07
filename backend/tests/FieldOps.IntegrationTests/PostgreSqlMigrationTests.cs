using System.Net;
using FieldOps.Infrastructure.Identity;
using FieldOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FieldOps.IntegrationTests;

public sealed class PostgreSqlMigrationTests
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task LineageUpgradePreservesLegacyRowsAndOnlyRevokesAccountsWithUntraceableSwitches()
    {
        await using var testDatabase = await TestDatabase.CreateAsync();
        await using var factory = new ApiFactory(connectionString: testDatabase.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
        var identity = Assert.Single(database.Database.GetMigrations(),
            migration => migration.EndsWith("_IdentityAndOrganizations", StringComparison.Ordinal));
        await database.GetService<IMigrator>().MigrateAsync(identity);

        var switchedUser = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();
        var oldSession = Guid.NewGuid();
        var successorSession = Guid.NewGuid();
        var otherSession = Guid.NewGuid();
        var oldRefresh = Guid.NewGuid();
        var rotatedRefresh = Guid.NewGuid();
        var successorRefresh = Guid.NewGuid();
        var otherRefresh = Guid.NewGuid();
        var passwordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<FieldOpsUser>>()
            .HashPassword(new FieldOpsUser(), "Legacy-Confirmed-Password-42!");
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AspNetUsers" ("Id", "DisplayName", "IsActive", "CreatedAt", "Email", "NormalizedEmail",
                "EmailConfirmed", "PasswordHash", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ({switchedUser}, 'Legacy switched', true, now(), 'switched@example.test', 'SWITCHED@EXAMPLE.TEST', true, {passwordHash}, false, false, true, 0),
                   ({otherUser}, 'Legacy other', true, now(), 'other@example.test', 'OTHER@EXAMPLE.TEST', true, {passwordHash}, false, false, true, 0);
            INSERT INTO "Organizations" ("Id", "Name", "TimeZoneId", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES ({firstTenant}, 'First', 'UTC', true, now(), now()), ({secondTenant}, 'Second', 'UTC', true, now(), now());
            INSERT INTO "OrganizationMemberships" ("Id", "TenantId", "UserId", "Role", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES (gen_random_uuid(), {firstTenant}, {switchedUser}, 'OrganizationOwner', true, now(), now()),
                   (gen_random_uuid(), {secondTenant}, {switchedUser}, 'Technician', true, now(), now()),
                   (gen_random_uuid(), {secondTenant}, {otherUser}, 'OrganizationOwner', true, now(), now());
            INSERT INTO "AuthSessions" ("Id", "UserId", "TenantId", "Transport", "SecurityStamp", "CreatedAt", "LastUsedAt",
                "ExpiresAt", "RevokedAt", "RevocationReason")
            VALUES ({oldSession}, {switchedUser}, {firstTenant}, 'Native', 'stamp', now(), now(), now() + interval '1 day', now(), 'organization_changed'),
                   ({successorSession}, {switchedUser}, {secondTenant}, 'Native', 'stamp', now(), now(), now() + interval '1 day', NULL, NULL),
                   ({otherSession}, {otherUser}, {secondTenant}, 'Web', 'stamp', now(), now(), now() + interval '1 day', NULL, NULL);
            INSERT INTO "RefreshCredentials" ("Id", "SessionId", "TokenHash", "CreatedAt", "ExpiresAt", "ConsumedAt", "ReplacedById")
            VALUES ({oldRefresh}, {oldSession}, repeat('A', 64), now(), now() + interval '1 day', now(), {rotatedRefresh}),
                   ({rotatedRefresh}, {oldSession}, repeat('B', 64), now(), now() + interval '1 day', NULL, NULL),
                   ({successorRefresh}, {successorSession}, repeat('C', 64), now(), now() + interval '1 day', NULL, NULL),
                   ({otherRefresh}, {otherSession}, repeat('D', 64), now(), now() + interval '1 day', NULL, NULL);
            """);

        var lineage = Assert.Single(database.Database.GetMigrations(),
            migration => migration.EndsWith("_RefreshSessionLineage", StringComparison.Ordinal));
        await database.GetService<IMigrator>().MigrateAsync(lineage);
        var sessions = await database.AuthSessions.AsNoTracking().ToDictionaryAsync(session => session.Id);
        Assert.Equal(3, sessions.Count);
        Assert.All(sessions.Values, session => Assert.Equal(session.Id, session.FamilyId));
        Assert.Equal("organization_changed", sessions[oldSession].RevocationReason);
        Assert.Equal("legacy_switch_reauthentication", sessions[successorSession].RevocationReason);
        Assert.NotNull(sessions[successorSession].RevokedAt);
        Assert.Null(sessions[otherSession].RevokedAt);
        var credentials = await database.RefreshCredentials.AsNoTracking().ToDictionaryAsync(token => token.Id);
        Assert.Equal(4, credentials.Count);
        Assert.All(credentials.Values, credential => Assert.Equal(credential.SessionId, credential.FamilyId));
        Assert.Equal(rotatedRefresh, credentials[oldRefresh].ReplacedById);
        Assert.Equal(new string('D', 64), credentials[otherRefresh].TokenHash);
        Assert.Equal(2, await database.Users.CountAsync());
        Assert.Equal(2, await database.Organizations.IgnoreQueryFilters().CountAsync());
        Assert.Equal(3, await database.OrganizationMemberships.IgnoreQueryFilters().CountAsync());

        var crossFamily = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "RefreshCredentials" SET "ReplacedById" = {otherRefresh} WHERE "Id" = {oldRefresh};
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, crossFamily.SqlState);
        await Assert.ThrowsAsync<NotSupportedException>(() => database.GetService<IMigrator>().MigrateAsync(identity));
        await database.Database.MigrateAsync();
        Assert.Empty(await database.Database.GetPendingMigrationsAsync());
        Assert.Equal(2, await database.Users.CountAsync());
        Assert.Equal(2, await database.Organizations.IgnoreQueryFilters().CountAsync());
        Assert.Equal(3, await database.OrganizationMemberships.IgnoreQueryFilters().CountAsync());
        Assert.Empty(await database.WorkOrders.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await database.Customers.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task BaselineMigratesIdempotentlyAndControlsReadiness()
    {
        await using var testDatabase = await TestDatabase.CreateAsync();
        await using var factory = new ApiFactory(connectionString: testDatabase.ConnectionString);
        using var client = factory.CreateClient();
        using var beforeMigration = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, beforeMigration.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
        var baseline = Assert.Single(database.Database.GetMigrations(),
            migration => migration.EndsWith("_InitialFoundation", StringComparison.Ordinal));
        await database.GetService<IMigrator>().MigrateAsync(baseline);
        Assert.Equal([baseline], await database.Database.GetAppliedMigrationsAsync());

        // Retain the Phase 0 guarantee: its original migration creates no application tables.
        await using (var connection = new NpgsqlConnection(testDatabase.ConnectionString))
        {
            await connection.OpenAsync();
            await using var tables = new NpgsqlCommand(
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name", connection);
            await using var reader = await tables.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("__EFMigrationsHistory", reader.GetString(0));
            Assert.False(await reader.ReadAsync());
        }

        using var baselineOnly = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, baselineOnly.StatusCode);

        await database.Database.MigrateAsync();
        await database.Database.MigrateAsync();
        var applied = (await database.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.True(applied.Length >= 2, "Phase 1 adds a separate migration without changing the Phase 0 baseline.");
        Assert.Equal(database.Database.GetMigrations(), applied);
        Assert.Empty(await database.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(database.Model.GetEntityTypes());
        using var afterMigration = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, afterMigration.StatusCode);
    }
}
