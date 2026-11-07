using FieldOps.Domain.Organizations;

namespace FieldOps.Domain.Operations;

public sealed class AuditLog : ITenantEntity
{
    private AuditLog() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string ResourceType { get; private set; } = "";
    public Guid ResourceId { get; private set; }
    public string Action { get; private set; } = "";
    public string? Reason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public WorkOrderAuditMetadata? Metadata { get; private set; }

    public static AuditLog Record(Guid tenantId, Guid actorUserId, string resourceType, Guid resourceId,
        string action, string? reason, DateTimeOffset occurredAt, WorkOrderAuditMetadata? metadata = null) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = actorUserId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Action = action,
            Reason = reason,
            OccurredAt = occurredAt.ToUniversalTime(),
            Metadata = metadata
        };
}
