using FieldOps.Domain.Operations;

namespace FieldOps.Application.Operations;

public sealed record AuditLogFilter(int? Page, int? PageSize, string? ResourceType, Guid? ResourceId,
    string? Action, Guid? ActorUserId, DateTimeOffset? From, DateTimeOffset? To);

public sealed record AuditLogDto(Guid Id, Guid ActorUserId, string ResourceType, Guid ResourceId,
    string Action, string? Reason, DateTimeOffset OccurredAt, WorkOrderAuditMetadata? Metadata);

public interface IAuditLogReader
{
    Task<PageResult<AuditLogDto>> ListAsync(AuditLogFilter filter, CancellationToken cancellationToken);
}
