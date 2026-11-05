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
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Health checks receive their own scope. Bound this probe's physical connection
            // timeout too: cancellation alone does not bound every PostgreSQL handshake.
            var connection = new NpgsqlConnectionStringBuilder(dbContext.Database.GetConnectionString());
            connection.Timeout = connection.Timeout is > 0 and < 3 ? connection.Timeout : 3;
            dbContext.Database.SetConnectionString(connection.ConnectionString);
            dbContext.Database.SetCommandTimeout(3);

            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("Database is unavailable.");
            }

            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
            return pendingMigrations.Any()
                ? HealthCheckResult.Unhealthy("Database migrations are pending.")
                : HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Database readiness failed with {FailureType}", exception.GetType().Name);
            return HealthCheckResult.Unhealthy("Database is unavailable.");
        }
    }
}
