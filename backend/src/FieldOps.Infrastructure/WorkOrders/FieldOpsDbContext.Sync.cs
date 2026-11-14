using FieldOps.Domain.Organizations;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

public sealed partial class FieldOpsDbContext
{
    internal DbSet<SyncReceipt> SyncReceipts => Set<SyncReceipt>();
    internal DbSet<SyncWorkOrderChange> SyncWorkOrderChanges => Set<SyncWorkOrderChange>();
    internal DbSet<SyncWatermark> SyncWatermarks => Set<SyncWatermark>();

    private void ConfigureSync(ModelBuilder modelBuilder)
    {
        var receipts = modelBuilder.Entity<SyncReceipt>();
        receipts.ToTable("SyncReceipts");
        receipts.HasKey(row => new { row.TenantId, row.UserId, row.OperationId });
        receipts.Property(row => row.Fingerprint).HasMaxLength(64).IsRequired();
        receipts.HasQueryFilter(row => IsSystemScope || row.TenantId == CurrentTenantId);
        receipts.HasOne<OrganizationMembership>().WithMany().HasForeignKey(row => new { row.TenantId, row.UserId })
            .HasPrincipalKey(member => new { member.TenantId, member.UserId }).OnDelete(DeleteBehavior.Restrict);
        receipts.HasOne<WorkOrder>().WithMany().HasForeignKey(row => new { row.TenantId, row.WorkOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id }).OnDelete(DeleteBehavior.Restrict);

        var changes = modelBuilder.Entity<SyncWorkOrderChange>();
        changes.ToTable("SyncWorkOrderChanges");
        changes.HasKey(row => new { row.TenantId, row.WorkOrderId });
        changes.HasIndex(row => new { row.TenantId, row.Position }).IsUnique();
        changes.HasQueryFilter(row => IsSystemScope || row.TenantId == CurrentTenantId);
        changes.HasOne<WorkOrder>().WithMany().HasForeignKey(row => new { row.TenantId, row.WorkOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id }).OnDelete(DeleteBehavior.Cascade);

        var watermarks = modelBuilder.Entity<SyncWatermark>();
        watermarks.ToTable("SyncWatermarks");
        watermarks.HasKey(row => row.TenantId);
        watermarks.HasQueryFilter(row => IsSystemScope || row.TenantId == CurrentTenantId);
        watermarks.HasOne<Organization>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed record SyncReceipt(Guid TenantId, Guid UserId, Guid OperationId, Guid WorkOrderId,
    string Fingerprint, long Version, DateTimeOffset ClientCreatedAt, DateTimeOffset AppliedAt) : ITenantEntity;

internal sealed record SyncWorkOrderChange(Guid TenantId, Guid WorkOrderId, long Position) : ITenantEntity;

internal sealed record SyncWatermark(Guid TenantId, long Position) : ITenantEntity;
