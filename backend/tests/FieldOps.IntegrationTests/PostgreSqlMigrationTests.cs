using System.Net;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.IntegrationTests;

public sealed class PostgreSqlMigrationTests
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task BaselineMigratesIdempotentlyAndControlsReadiness()
    {
        var connectionString = Environment.GetEnvironmentVariable("FIELDOPS_TEST_DATABASE");
        Assert.False(string.IsNullOrWhiteSpace(connectionString),
            "Set FIELDOPS_TEST_DATABASE to a local PostgreSQL connection with CREATE DATABASE permission. " +
            "Use --filter Category!=Database for explicitly database-free verification.");

        var adminSettings = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        var databaseName = $"fieldops_test_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(adminSettings.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }

        var testSettings = new NpgsqlConnectionStringBuilder(adminSettings.ConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };

        try
        {
            await using var factory = new ApiFactory(connectionString: testSettings.ConnectionString);
            using var client = factory.CreateClient();
            using var beforeMigration = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, beforeMigration.StatusCode);

            var options = new DbContextOptionsBuilder<FieldOpsDbContext>()
                .UseNpgsql(testSettings.ConnectionString).Options;
            await using var database = new FieldOpsDbContext(options);
            await database.Database.MigrateAsync();
            await database.Database.MigrateAsync();

            Assert.Single(await database.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await database.Database.GetPendingMigrationsAsync());
            Assert.Empty(database.Model.GetEntityTypes());
            using var afterMigration = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, afterMigration.StatusCode);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
