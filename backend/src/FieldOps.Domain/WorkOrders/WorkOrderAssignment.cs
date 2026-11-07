using FieldOps.Domain.Organizations;

namespace FieldOps.Domain.WorkOrders;

public sealed class WorkOrderAssignment : ITenantEntity
{
    private WorkOrderAssignment() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public Guid TechnicianUserId { get; private set; }
    public bool IsPrimary { get; private set; }
    public Guid AssignedByUserId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }

    public static WorkOrderAssignment Create(WorkOrder order, Guid technicianUserId, bool isPrimary,
        Guid assignedByUserId, DateTimeOffset now)
    {
        if (technicianUserId == Guid.Empty || assignedByUserId == Guid.Empty)
            throw new ArgumentException("The technician and assigning actor are required.");
        return new WorkOrderAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = order.TenantId,
            WorkOrderId = order.Id,
            TechnicianUserId = technicianUserId,
            IsPrimary = isPrimary,
            AssignedByUserId = assignedByUserId,
            AssignedAt = now.ToUniversalTime()
        };
    }

    public void Retire(DateTimeOffset now) => RemovedAt ??= now < AssignedAt ? AssignedAt : now.ToUniversalTime();

    public static bool Overlaps(DateTimeOffset start, DateTimeOffset end, DateTimeOffset otherStart, DateTimeOffset otherEnd)
        => start < otherEnd && otherStart < end;
}
