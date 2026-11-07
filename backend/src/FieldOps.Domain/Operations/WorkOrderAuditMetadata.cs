using System.Collections.Immutable;

namespace FieldOps.Domain.Operations;

public sealed record AuditValueChange<T>(T Previous, T Current);

public sealed record WorkOrderAuditChanges(
    AuditValueChange<DateTimeOffset?>? PlannedStartAt,
    AuditValueChange<DateTimeOffset?>? PlannedEndAt,
    AuditValueChange<ImmutableArray<Guid>>? TechnicianUserIds,
    AuditValueChange<Guid?>? PrimaryTechnicianUserId);

public sealed record WorkOrderAuditMetadata(long PreviousVersion, long EntityVersion, WorkOrderAuditChanges Changes);
