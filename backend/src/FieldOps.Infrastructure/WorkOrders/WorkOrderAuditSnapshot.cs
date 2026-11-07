using System.Collections.Immutable;
using FieldOps.Domain.Operations;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.WorkOrders;

// Read only the allowlisted operational fields from PostgreSQL, including its timestamp precision.
internal sealed record WorkOrderAuditSnapshot(long Version, DateTimeOffset? PlannedStartAt,
    DateTimeOffset? PlannedEndAt, ImmutableArray<Guid> TechnicianUserIds, Guid? PrimaryTechnicianUserId)
{
    public static async Task<WorkOrderAuditSnapshot> ReadAsync(FieldOpsDbContext database, Guid tenantId,
        Guid orderId, CancellationToken cancellationToken)
    {
        var order = await database.WorkOrders.AsNoTracking()
            .Where(order => order.TenantId == tenantId && order.Id == orderId)
            .Select(order => new { order.Version, order.PlannedStartAt, order.PlannedEndAt })
            .SingleAsync(cancellationToken);
        var assignments = await database.WorkOrderAssignments.AsNoTracking()
            .Where(assignment => assignment.TenantId == tenantId && assignment.WorkOrderId == orderId && assignment.RemovedAt == null)
            .OrderBy(assignment => assignment.TechnicianUserId)
            .Select(assignment => new { assignment.TechnicianUserId, assignment.IsPrimary }).ToListAsync(cancellationToken);
        return new WorkOrderAuditSnapshot(order.Version, order.PlannedStartAt, order.PlannedEndAt,
            assignments.Select(assignment => assignment.TechnicianUserId).ToImmutableArray(),
            assignments.Where(assignment => assignment.IsPrimary).Select(assignment => (Guid?)assignment.TechnicianUserId).SingleOrDefault());
    }

    public WorkOrderAuditMetadata ChangesTo(WorkOrderAuditSnapshot current) => new(Version, current.Version,
        new WorkOrderAuditChanges(
            Change(PlannedStartAt, current.PlannedStartAt),
            Change(PlannedEndAt, current.PlannedEndAt),
            TechnicianUserIds.SequenceEqual(current.TechnicianUserIds) ? null :
                new AuditValueChange<ImmutableArray<Guid>>(TechnicianUserIds, current.TechnicianUserIds),
            Change(PrimaryTechnicianUserId, current.PrimaryTechnicianUserId)));

    private static AuditValueChange<T>? Change<T>(T previous, T current) =>
        EqualityComparer<T>.Default.Equals(previous, current) ? null : new AuditValueChange<T>(previous, current);
}
