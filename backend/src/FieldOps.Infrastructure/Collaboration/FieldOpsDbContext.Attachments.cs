using FieldOps.Domain.Organizations;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

public sealed partial class FieldOpsDbContext
{
    internal DbSet<WorkOrderAttachment> WorkOrderAttachments => Set<WorkOrderAttachment>();

    private void ConfigureAttachments(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkOrderAttachment>();
        entity.HasKey(row => row.Id);
        entity.Property(row => row.FileName).HasMaxLength(160);
        entity.Property(row => row.ContentType).HasMaxLength(64);
        entity.Property(row => row.Hash).HasMaxLength(64);
        entity.HasIndex(row => new { row.TenantId, row.WorkOrderId, row.UploaderId, row.OperationId }).IsUnique();
        entity.HasIndex(row => new { row.TenantId, row.WorkOrderId, row.CreatedAt });
        entity.HasIndex(row => row.CreatedAt).HasFilter("\"Ready\" = false");
        entity.HasQueryFilter(row => IsSystemScope || row.TenantId == CurrentTenantId);
        entity.HasOne<WorkOrder>().WithMany().HasForeignKey(row => new { row.TenantId, Id = row.WorkOrderId })
            .HasPrincipalKey(row => new { row.TenantId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<OrganizationMembership>().WithMany().HasForeignKey(row => new { row.TenantId, UserId = row.UploaderId })
            .HasPrincipalKey(row => new { row.TenantId, row.UserId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed record WorkOrderAttachment(Guid Id, Guid TenantId, Guid WorkOrderId, Guid UploaderId, Guid OperationId,
    string FileName, string ContentType, string Hash, long Size, DateTimeOffset CreatedAt) : ITenantEntity
{
    public bool Ready { get; set; }
    public string ObjectKey() => $"{TenantId:N}/{WorkOrderId:N}/{Id:N}";
}
