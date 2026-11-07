using FieldOps.Domain.Catalog;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

public sealed partial class FieldOpsDbContext
{
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderAssignment> WorkOrderAssignments => Set<WorkOrderAssignment>();
    public DbSet<WorkOrderStatusHistory> WorkOrderStatusHistory => Set<WorkOrderStatusHistory>();

    partial void ConfigureWorkOrders(ModelBuilder modelBuilder)
    {
        var orders = modelBuilder.Entity<WorkOrder>();
        orders.ToTable("WorkOrders", table =>
        {
            table.HasCheckConstraint("CK_WorkOrders_Version", """ "Version" > 0 """);
            table.HasCheckConstraint("CK_WorkOrders_Priority", """ "Priority" IN ('Low', 'Medium', 'High', 'Critical') """);
            table.HasCheckConstraint("CK_WorkOrders_Status", """ "Status" IN ('Draft', 'Scheduled', 'Assigned', 'InProgress', 'OnHold', 'Completed', 'Cancelled') """);
            table.HasCheckConstraint("CK_WorkOrders_PlannedInterval", """
                ("PlannedStartAt" IS NULL AND "PlannedEndAt" IS NULL AND "Status" IN ('Draft', 'Cancelled'))
                OR ("PlannedStartAt" IS NOT NULL AND "PlannedEndAt" IS NOT NULL AND "PlannedStartAt" < "PlannedEndAt" AND "Status" <> 'Draft')
                """);
            table.HasCheckConstraint("CK_WorkOrders_ActualInterval", """
                ("ActualStartAt" IS NULL AND "ActualEndAt" IS NULL AND "Status" IN ('Draft', 'Scheduled', 'Assigned', 'Cancelled'))
                OR ("ActualStartAt" IS NOT NULL AND "ActualEndAt" IS NULL AND "Status" IN ('InProgress', 'OnHold'))
                OR ("ActualStartAt" IS NOT NULL AND "ActualEndAt" IS NOT NULL AND "ActualStartAt" <= "ActualEndAt" AND "Status" IN ('Completed', 'Cancelled'))
                """);
        });
        orders.HasKey(order => order.Id);
        orders.HasAlternateKey(order => new { order.TenantId, order.Id });
        orders.Property(order => order.Version).IsConcurrencyToken();
        orders.Property(order => order.WorkOrderNumber).HasMaxLength(35).IsRequired();
        orders.Property(order => order.Title).HasMaxLength(160).IsRequired();
        orders.Property(order => order.Description).HasMaxLength(4000);
        orders.Property(order => order.Status).HasConversion<string>().HasMaxLength(16);
        orders.Property(order => order.Priority).HasConversion<string>().HasMaxLength(8);
        orders.HasIndex(order => new { order.TenantId, order.WorkOrderNumber }).IsUnique();
        orders.HasIndex(order => new { order.TenantId, order.CreatedAt, order.Id });
        orders.HasIndex(order => new { order.TenantId, order.Status, order.PlannedStartAt });
        orders.HasIndex(order => new { order.TenantId, order.AssetId, order.ActualEndAt });
        orders.HasOne<Customer>().WithMany()
            .HasForeignKey(order => new { order.TenantId, order.CustomerId })
            .HasPrincipalKey(customer => new { customer.TenantId, customer.Id }).OnDelete(DeleteBehavior.Restrict);
        orders.HasOne<ServiceLocation>().WithMany()
            .HasForeignKey(order => new { order.TenantId, order.CustomerId, order.ServiceLocationId })
            .HasPrincipalKey(location => new { location.TenantId, location.CustomerId, location.Id }).OnDelete(DeleteBehavior.Restrict);
        orders.HasOne<Asset>().WithMany()
            .HasForeignKey(order => new { order.TenantId, order.CustomerId, order.ServiceLocationId, order.AssetId })
            .HasPrincipalKey(asset => new { asset.TenantId, asset.CustomerId, asset.ServiceLocationId, asset.Id }).OnDelete(DeleteBehavior.Restrict);
        orders.HasOne<OrganizationMembership>().WithMany()
            .HasForeignKey(order => new { order.TenantId, order.CreatedByUserId })
            .HasPrincipalKey(membership => new { membership.TenantId, membership.UserId }).OnDelete(DeleteBehavior.Restrict);
        orders.HasQueryFilter(order => IsSystemScope || order.TenantId == CurrentTenantId);

        var assignments = modelBuilder.Entity<WorkOrderAssignment>();
        assignments.ToTable("WorkOrderAssignments", table => table.HasCheckConstraint("CK_WorkOrderAssignments_RemovedAt",
            """ "RemovedAt" IS NULL OR "AssignedAt" <= "RemovedAt" """));
        assignments.HasKey(assignment => assignment.Id);
        assignments.HasIndex(assignment => new { assignment.TenantId, assignment.WorkOrderId, assignment.TechnicianUserId })
            .IsUnique().HasFilter(""" "RemovedAt" IS NULL """);
        assignments.HasIndex(assignment => new { assignment.TenantId, assignment.WorkOrderId })
            .IsUnique().HasFilter(""" "IsPrimary" AND "RemovedAt" IS NULL """);
        assignments.HasIndex(assignment => new { assignment.TenantId, assignment.TechnicianUserId, assignment.WorkOrderId })
            .HasFilter(""" "RemovedAt" IS NULL """);
        assignments.HasOne<WorkOrder>().WithMany()
            .HasForeignKey(assignment => new { assignment.TenantId, assignment.WorkOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id }).OnDelete(DeleteBehavior.Restrict);
        assignments.HasOne<OrganizationMembership>().WithMany()
            .HasForeignKey(assignment => new { assignment.TenantId, assignment.TechnicianUserId })
            .HasPrincipalKey(membership => new { membership.TenantId, membership.UserId }).OnDelete(DeleteBehavior.Restrict);
        assignments.HasOne<OrganizationMembership>().WithMany()
            .HasForeignKey(assignment => new { assignment.TenantId, assignment.AssignedByUserId })
            .HasPrincipalKey(membership => new { membership.TenantId, membership.UserId }).OnDelete(DeleteBehavior.Restrict);
        assignments.HasQueryFilter(assignment => IsSystemScope || assignment.TenantId == CurrentTenantId);

        var history = modelBuilder.Entity<WorkOrderStatusHistory>();
        history.ToTable("WorkOrderStatusHistories");
        history.HasKey(entry => entry.Id);
        history.Property(entry => entry.Status).HasConversion<string>().HasMaxLength(16);
        history.Property(entry => entry.PreviousStatus).HasConversion<string>().HasMaxLength(16);
        history.Property(entry => entry.Reason).HasMaxLength(500);
        history.HasIndex(entry => new { entry.TenantId, entry.WorkOrderId, entry.WorkOrderVersion }).IsUnique();
        history.HasOne<WorkOrder>().WithMany()
            .HasForeignKey(entry => new { entry.TenantId, entry.WorkOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id }).OnDelete(DeleteBehavior.Restrict);
        history.HasOne<OrganizationMembership>().WithMany()
            .HasForeignKey(entry => new { entry.TenantId, entry.ChangedByUserId })
            .HasPrincipalKey(membership => new { membership.TenantId, membership.UserId }).OnDelete(DeleteBehavior.Restrict);
        history.HasQueryFilter(entry => IsSystemScope || entry.TenantId == CurrentTenantId);
    }
}
