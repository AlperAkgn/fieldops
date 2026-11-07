using FieldOps.Domain.Organizations;

namespace FieldOps.Domain.WorkOrders;

public sealed class WorkOrderStatusHistory : ITenantEntity
{
    private WorkOrderStatusHistory() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public WorkOrderStatus? PreviousStatus { get; private set; }
    public WorkOrderStatus Status { get; private set; }
    public Guid ChangedByUserId { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public long WorkOrderVersion { get; private set; }
    public string? Reason { get; private set; }

    public static WorkOrderStatusHistory Create(WorkOrder order, WorkOrderStatus? previousStatus, Guid actorId,
        DateTimeOffset now, string? reason) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = order.TenantId,
            WorkOrderId = order.Id,
            PreviousStatus = previousStatus,
            Status = order.Status,
            ChangedByUserId = actorId,
            ChangedAt = now.ToUniversalTime(),
            WorkOrderVersion = order.Version,
            Reason = reason
        };
}
