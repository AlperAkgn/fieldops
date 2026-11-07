using FieldOps.Domain.Operations;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FieldOps.Infrastructure.Persistence;

public sealed partial class FieldOpsDbContext
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    private void ConfigureAudit(ModelBuilder modelBuilder)
    {
        var audit = modelBuilder.Entity<AuditLog>();
        audit.ToTable("AuditLogs", table => table.HasCheckConstraint("CK_AuditLogs_Metadata",
            """ "Metadata" IS NULL OR jsonb_typeof("Metadata") = 'object' """));
        audit.HasQueryFilter(log => IsSystemScope || log.TenantId == CurrentTenantId);
        audit.HasAlternateKey(log => new { log.TenantId, log.Id });
        audit.Property(log => log.ResourceType).HasMaxLength(64).IsRequired();
        audit.Property(log => log.Action).HasMaxLength(64).IsRequired();
        audit.Property(log => log.Reason).HasMaxLength(1000);
        audit.Property(log => log.Metadata).HasColumnType("jsonb").HasConversion(
            metadata => JsonSerializer.Serialize(metadata, JsonSerializerOptions.Web),
            json => JsonSerializer.Deserialize<WorkOrderAuditMetadata>(json, JsonSerializerOptions.Web));
        audit.HasIndex(log => new { log.TenantId, log.OccurredAt, log.Id });
        audit.HasIndex(log => new { log.TenantId, log.ResourceType, log.ResourceId, log.OccurredAt });
        audit.HasOne<OrganizationMembership>().WithMany()
            .HasForeignKey(log => new { log.TenantId, log.ActorUserId })
            .HasPrincipalKey(member => new { member.TenantId, member.UserId }).OnDelete(DeleteBehavior.Restrict);
    }
}
