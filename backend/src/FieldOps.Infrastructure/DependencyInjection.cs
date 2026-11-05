using FieldOps.Infrastructure.Health;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<FieldOpsDbContext>(options =>
            options.UseNpgsql(connectionString, database => database.CommandTimeout(5)));
        services.AddHealthChecks().AddCheck<PostgreSqlReadinessCheck>(
            "postgresql", tags: ["ready"], timeout: TimeSpan.FromSeconds(5));

        return services;
    }
}
