using FieldOps.Domain.Identity;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

public sealed partial class FieldOpsDbContext
{
    internal DbSet<WorkOrderEvent> WorkOrderEvents => Set<WorkOrderEvent>();
    internal DbSet<InboxNotification> InboxNotifications => Set<InboxNotification>();
    internal DbSet<PushDevice> PushDevices => Set<PushDevice>();
    internal DbSet<PushDelivery> PushDeliveries => Set<PushDelivery>();

    private void ConfigureCollaboration(ModelBuilder modelBuilder)
    {
        var changes = modelBuilder.Entity<WorkOrderEvent>();
        changes.HasKey(row => row.Id);
        changes.Property(row => row.Kind).HasMaxLength(64);
        changes.HasIndex(row => new { row.TenantId, row.WorkOrderId, row.Version });
        changes.HasIndex(row => new { row.CreatedAt, row.Id }).HasFilter("\"DispatchedAt\" IS NULL");
        changes.HasQueryFilter(row => IsSystemScope || row.TenantId == CurrentTenantId);
        changes.HasOne<WorkOrder>().WithMany().HasForeignKey(row => new { row.TenantId, Id = row.WorkOrderId })
            .HasPrincipalKey(row => new { row.TenantId, row.Id }).OnDelete(DeleteBehavior.Restrict);

        var inbox = modelBuilder.Entity<InboxNotification>();
        inbox.HasKey(row => row.Id);
        inbox.HasAlternateKey(row => new { row.TenantId, row.Id });
        inbox.Property(row => row.Kind).HasMaxLength(64);
        inbox.HasIndex(row => new { row.TenantId, row.UserId, row.CreatedAt });
        inbox.HasIndex(row => new { row.TenantId, row.UserId, row.WorkOrderId, row.Version, row.Kind }).IsUnique();
        inbox.HasQueryFilter(row => IsSystemScope || row.TenantId == CurrentTenantId);
        inbox.HasOne<WorkOrder>().WithMany().HasForeignKey(row => new { row.TenantId, Id = row.WorkOrderId })
            .HasPrincipalKey(row => new { row.TenantId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        inbox.HasOne<OrganizationMembership>().WithMany().HasForeignKey(row => new { row.TenantId, row.UserId })
            .HasPrincipalKey(row => new { row.TenantId, row.UserId }).OnDelete(DeleteBehavior.Restrict);

        var device = modelBuilder.Entity<PushDevice>();
        device.HasKey(row => new { row.TenantId, row.DeviceId, row.SessionId });
        device.HasIndex(row => row.TokenHash).IsUnique().HasFilter("\"RevokedAt\" IS NULL");
        device.Property(row => row.TokenHash).HasMaxLength(64);
        device.HasQueryFilter(row => IsSystemScope || row.TenantId == CurrentTenantId);
        device.HasOne<AuthSession>().WithMany().HasForeignKey(row => row.SessionId).OnDelete(DeleteBehavior.Cascade);
        device.HasOne<OrganizationMembership>().WithMany().HasForeignKey(row => new { row.TenantId, row.UserId })
            .HasPrincipalKey(row => new { row.TenantId, row.UserId }).OnDelete(DeleteBehavior.Restrict);

        var delivery = modelBuilder.Entity<PushDelivery>();
        delivery.HasKey(row => row.Id);
        delivery.HasIndex(row => new { row.TenantId, row.NotificationId, row.DeviceId, row.SessionId }).IsUnique();
        delivery.HasIndex(row => row.DueAt).HasFilter("\"CompletedAt\" IS NULL");
        delivery.Property(row => row.TicketId).HasMaxLength(128);
        delivery.Property(row => row.Outcome).HasMaxLength(64);
        delivery.HasQueryFilter(row => IsSystemScope || row.TenantId == CurrentTenantId);
        delivery.HasOne<InboxNotification>().WithMany().HasForeignKey(row => new { row.TenantId, Id = row.NotificationId })
            .HasPrincipalKey(row => new { row.TenantId, row.Id }).OnDelete(DeleteBehavior.Cascade);
        delivery.HasOne<PushDevice>().WithMany().HasForeignKey(row => new { row.TenantId, row.DeviceId, row.SessionId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed record WorkOrderEvent(Guid Id, Guid TenantId, Guid WorkOrderId, long Version, string Kind,
    DateTimeOffset CreatedAt, Guid[] FormerAssignees) : ITenantEntity
{
    public DateTimeOffset? DispatchedAt { get; set; }
}

internal sealed record InboxNotification(Guid Id, Guid TenantId, Guid UserId, Guid WorkOrderId, long Version,
    string Kind, DateTimeOffset CreatedAt) : ITenantEntity
{
    public DateTimeOffset? ReadAt { get; set; }
}

internal sealed record PushDevice(Guid TenantId, Guid DeviceId, Guid SessionId, Guid UserId) : ITenantEntity
{
    public string TokenHash { get; set; } = "";
    public string ProtectedToken { get; set; } = "";
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

internal sealed record PushDelivery(Guid Id, Guid TenantId, Guid NotificationId, Guid DeviceId, Guid SessionId,
    string TokenHash, DateTimeOffset CreatedAt) : ITenantEntity
{
    public string TokenHash { get; set; } = TokenHash;
    public DateTimeOffset DueAt { get; set; } = CreatedAt;
    public DateTimeOffset? CompletedAt { get; set; }
    public int Attempts { get; set; }
    public string? TicketId { get; set; }
    public string? Outcome { get; set; }
}
