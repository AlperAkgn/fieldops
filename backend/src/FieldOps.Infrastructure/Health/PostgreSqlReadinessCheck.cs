using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FieldOps.Infrastructure.Health;

public sealed class PostgreSqlReadinessCheck(
    FieldOpsDbContext dbContext,
    ILogger<PostgreSqlReadinessCheck> logger) : IHealthCheck
{
    public const string Name = "postgresql";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(3500));
        try
        {
            // Probe with one fresh physical connection, independent of the application pool.
            var connection = new NpgsqlConnectionStringBuilder(dbContext.Database.GetConnectionString());
            connection.Timeout = connection.Timeout is > 0 and < 2 ? connection.Timeout : 2;
            connection.CommandTimeout = connection.CommandTimeout is > 0 and < 2 ? connection.CommandTimeout : 2;
            connection.CancellationTimeout = -1;
            connection.Pooling = false;
            connection.Multiplexing = false;
            dbContext.Database.SetConnectionString(connection.ConnectionString);
            dbContext.Database.SetCommandTimeout(connection.CommandTimeout);

            await dbContext.Database.OpenConnectionAsync(deadline.Token);
            try
            {
                var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(deadline.Token);
                return pendingMigrations.Any()
                    ? HealthCheckResult.Unhealthy("Database migrations are pending.")
                    : HealthCheckResult.Healthy();
            }
            finally { await dbContext.Database.CloseConnectionAsync(); }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Database readiness deadline expired.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Database readiness failed with {FailureType}", exception.GetType().Name);
            return HealthCheckResult.Unhealthy("Database is unavailable.");
        }
    }
}
