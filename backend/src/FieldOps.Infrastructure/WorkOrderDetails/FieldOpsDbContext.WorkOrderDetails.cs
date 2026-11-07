using FieldOps.Domain.Organizations;
using FieldOps.Domain.WorkOrderDetails;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

public sealed partial class FieldOpsDbContext
{
    public DbSet<ChecklistTemplate> ChecklistTemplates => Set<ChecklistTemplate>();
    public DbSet<ChecklistTemplateItem> ChecklistTemplateItems => Set<ChecklistTemplateItem>();
    public DbSet<WorkOrderChecklistItem> WorkOrderChecklistItems => Set<WorkOrderChecklistItem>();
    public DbSet<WorkOrderComment> WorkOrderComments => Set<WorkOrderComment>();

    partial void ConfigureWorkOrderDetails(ModelBuilder modelBuilder)
    {
        var templates = modelBuilder.Entity<ChecklistTemplate>();
        templates.ToTable("ChecklistTemplates", table =>
        {
            table.HasCheckConstraint("CK_ChecklistTemplates_Name", "length(btrim(\"Name\")) BETWEEN 1 AND 120");
            table.HasCheckConstraint("CK_ChecklistTemplates_Version", "\"Version\" > 0");
        });
        templates.HasKey(template => template.Id);
        templates.HasAlternateKey(template => new { template.TenantId, template.Id });
        templates.Property(template => template.Name).HasMaxLength(ChecklistTemplate.MaximumNameLength).IsRequired();
        templates.Property(template => template.Version).IsConcurrencyToken();
        templates.HasIndex(template => new { template.TenantId, template.IsActive, template.Name, template.Id });
        templates.HasOne<Organization>().WithMany().HasForeignKey(template => template.TenantId).OnDelete(DeleteBehavior.Restrict);
        templates.HasQueryFilter(template => IsSystemScope || template.TenantId == CurrentTenantId);

        var items = modelBuilder.Entity<ChecklistTemplateItem>();
        items.ToTable("ChecklistTemplateItems", table =>
        {
            table.HasCheckConstraint("CK_ChecklistTemplateItems_Label", "length(btrim(\"Label\")) BETWEEN 1 AND 240");
            table.HasCheckConstraint("CK_ChecklistTemplateItems_Position", "\"Position\" BETWEEN 0 AND 999");
        });
        items.HasKey(item => item.Id);
        items.HasAlternateKey(item => new { item.TenantId, item.TemplateId, item.Id });
        items.Property(item => item.Label).HasMaxLength(ChecklistTemplateItem.MaximumLabelLength).IsRequired();
        items.HasIndex(item => new { item.TenantId, item.TemplateId, item.IsActive, item.Position, item.Id });
        items.HasOne<ChecklistTemplate>().WithMany()
            .HasForeignKey(item => new { item.TenantId, item.TemplateId })
            .HasPrincipalKey(template => new { template.TenantId, template.Id }).OnDelete(DeleteBehavior.Restrict);
        items.HasQueryFilter(item => IsSystemScope || item.TenantId == CurrentTenantId);

        var snapshots = modelBuilder.Entity<WorkOrderChecklistItem>();
        snapshots.ToTable("WorkOrderChecklistItems", table =>
        {
            table.HasCheckConstraint("CK_WorkOrderChecklistItems_Completion", """
                ("IsCompleted" AND "CompletedByUserId" IS NOT NULL AND "CompletedAt" IS NOT NULL) OR
                (NOT "IsCompleted" AND "CompletedByUserId" IS NULL AND "CompletedAt" IS NULL)
                """);
            table.HasCheckConstraint("CK_WorkOrderChecklistItems_SourceVersion", "\"SourceTemplateVersion\" > 0");
            table.HasCheckConstraint("CK_WorkOrderChecklistItems_Label", "length(btrim(\"Label\")) BETWEEN 1 AND 240");
            table.HasCheckConstraint("CK_WorkOrderChecklistItems_Position", "\"Position\" BETWEEN 0 AND 999");
        });
        snapshots.HasKey(item => item.Id);
        snapshots.Property(item => item.Label).HasMaxLength(ChecklistTemplateItem.MaximumLabelLength).IsRequired();
        snapshots.HasIndex(item => new { item.TenantId, item.WorkOrderId, item.SourceTemplateItemId }).IsUnique();
        snapshots.HasIndex(item => new { item.TenantId, item.WorkOrderId, item.Position, item.Id });
        snapshots.HasOne<WorkOrder>().WithMany()
            .HasForeignKey(item => new { item.TenantId, item.WorkOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id }).OnDelete(DeleteBehavior.Restrict);
        snapshots.HasOne<ChecklistTemplateItem>().WithMany()
            .HasForeignKey(item => new { item.TenantId, item.SourceTemplateId, item.SourceTemplateItemId })
            .HasPrincipalKey(item => new { item.TenantId, item.TemplateId, item.Id }).OnDelete(DeleteBehavior.Restrict);
        snapshots.HasOne<OrganizationMembership>().WithMany()
            .HasForeignKey(item => new { item.TenantId, item.CompletedByUserId })
            .HasPrincipalKey(member => new { member.TenantId, member.UserId }).OnDelete(DeleteBehavior.Restrict);
        snapshots.HasQueryFilter(item => IsSystemScope || item.TenantId == CurrentTenantId);

        var comments = modelBuilder.Entity<WorkOrderComment>();
        comments.ToTable("WorkOrderComments", table =>
        {
            table.HasCheckConstraint("CK_WorkOrderComments_Visibility", "\"Visibility\" IN ('TechnicianVisible', 'Internal')");
            table.HasCheckConstraint("CK_WorkOrderComments_Body", "length(btrim(\"Body\")) BETWEEN 1 AND 4000");
        });
        comments.HasKey(comment => comment.Id);
        comments.Property(comment => comment.Body).HasMaxLength(WorkOrderComment.MaximumBodyLength).IsRequired();
        comments.Property(comment => comment.Visibility).HasConversion<string>().HasMaxLength(24);
        comments.HasIndex(comment => new { comment.TenantId, comment.WorkOrderId, comment.CreatedAt, comment.Id });
        comments.HasOne<WorkOrder>().WithMany()
            .HasForeignKey(comment => new { comment.TenantId, comment.WorkOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id }).OnDelete(DeleteBehavior.Restrict);
        comments.HasOne<OrganizationMembership>().WithMany()
            .HasForeignKey(comment => new { comment.TenantId, comment.AuthorUserId })
            .HasPrincipalKey(member => new { member.TenantId, member.UserId }).OnDelete(DeleteBehavior.Restrict);
        comments.HasQueryFilter(comment => IsSystemScope || comment.TenantId == CurrentTenantId);
    }
}
