using System.Collections.Immutable;
using FieldOps.Application.Tenancy;
using FieldOps.Domain.Operations;
using FieldOps.Domain.Organizations;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.IntegrationTests;

public sealed class AuditMetadataMigrationTests
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task UpgradePreservesLegacyAuditWithoutInventingMetadataAndPersistsNewTypedChanges()
    {
        await using var testDatabase = await TestDatabase.CreateAsync();
        await using var factory = new ApiFactory(connectionString: testDatabase.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
        var core = Assert.Single(database.Database.GetMigrations(),
            name => name.EndsWith("_CoreOperations", StringComparison.Ordinal));
        await database.GetService<IMigrator>().MigrateAsync(core);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var auditId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AspNetUsers" ("Id", "DisplayName", "IsActive", "CreatedAt", "Email", "NormalizedEmail",
                "EmailConfirmed", "PasswordHash", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ({userId}, 'Legacy owner', true, {occurredAt}, 'legacy@example.test', 'LEGACY@EXAMPLE.TEST',
                true, 'legacy-password-hash', false, false, true, 0);
            INSERT INTO "Organizations" ("Id", "Name", "TimeZoneId", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES ({tenantId}, 'Legacy organization', 'UTC', true, {occurredAt}, {occurredAt});
            INSERT INTO "OrganizationMemberships" ("Id", "TenantId", "UserId", "Role", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES (gen_random_uuid(), {tenantId}, {userId}, 'OrganizationOwner', true, {occurredAt}, {occurredAt});
            INSERT INTO "AuditLogs" ("Id", "TenantId", "ActorUserId", "ResourceType", "ResourceId", "Action", "Reason", "OccurredAt")
            VALUES ({auditId}, {tenantId}, {userId}, 'work_order', {resourceId}, 'rescheduled', 'Legacy reason', {occurredAt});
            """);
        await database.Database.MigrateAsync();
        var legacy = await database.AuditLogs.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.Equal(auditId, legacy.Id);
        Assert.Equal(userId, legacy.ActorUserId);
        Assert.Equal(tenantId, legacy.TenantId);
        Assert.Equal(resourceId, legacy.ResourceId);
        Assert.Equal("rescheduled", legacy.Action);
        Assert.Equal("Legacy reason", legacy.Reason);
        Assert.Equal(occurredAt, legacy.OccurredAt);
        Assert.Null(legacy.Metadata);
        Assert.Equal(1, await database.Users.CountAsync());
        Assert.Equal(1, await database.Organizations.IgnoreQueryFilters().CountAsync());

        scope.ServiceProvider.GetRequiredService<TenantContext>().SetValidated(
            new TenantActor(userId, Guid.NewGuid(), tenantId, OrganizationRole.OrganizationOwner));
        var metadata = new WorkOrderAuditMetadata(2, 3, new WorkOrderAuditChanges(
            new AuditValueChange<DateTimeOffset?>(occurredAt, occurredAt.AddHours(1)), null,
            new AuditValueChange<ImmutableArray<Guid>>([], [userId]),
            new AuditValueChange<Guid?>(null, userId)));
        var current = AuditLog.Record(tenantId, userId, "work_order", resourceId, "rescheduled", null,
            occurredAt.AddHours(1), metadata);
        database.AuditLogs.Add(current);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var persisted = await database.AuditLogs.SingleAsync(log => log.Id == current.Id);
        Assert.Equal(3, persisted.Metadata!.EntityVersion);
        Assert.Equal(userId, Assert.Single(persisted.Metadata.Changes.TechnicianUserIds!.Current));
        Assert.Equal(occurredAt, persisted.Metadata.Changes.PlannedStartAt!.Previous);
        Assert.Equal(occurredAt.AddHours(1), persisted.Metadata.Changes.PlannedStartAt.Current);
        await database.Database.MigrateAsync();
        Assert.False(database.Database.HasPendingModelChanges());
        await Assert.ThrowsAsync<NotSupportedException>(() => database.GetService<IMigrator>().MigrateAsync(core));
        Assert.Equal(2, await database.AuditLogs.CountAsync());
        Assert.Null((await database.AuditLogs.AsNoTracking().SingleAsync(log => log.Id == auditId)).Metadata);
    }
}
