using FieldOps.Application.Common;
using FieldOps.Application.Operations;
using FieldOps.Domain.Organizations;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Operations;

internal sealed class AuditLogReader(FieldOpsDbContext database, OperationalWriteContext operations) : IAuditLogReader
{
    public async Task<PageResult<AuditLogDto>> ListAsync(AuditLogFilter filter, CancellationToken cancellationToken)
    {
        var actor = operations.Actor;
        if (actor.Role is not (OrganizationRole.OrganizationOwner or OrganizationRole.Manager))
            throw new FieldOpsException(ErrorKind.Forbidden, "audit_read_forbidden", "Audit logs require organization administration access.");

        var page = new PageRequest(filter.Page ?? 1, filter.PageSize ?? 25);
        page.Validate();
        if (filter.From >= filter.To || filter.ResourceType is { Length: > 64 } || filter.Action is { Length: > 64 }
            || filter.ResourceType?.Any(char.IsControl) == true || filter.Action?.Any(char.IsControl) == true)
            throw new FieldOpsException(ErrorKind.Validation, "invalid_audit_filter", "Provide valid audit filters and an increasing time range.");

        var query = database.AuditLogs.AsNoTracking().Where(log => log.TenantId == actor.TenantId);
        if (!string.IsNullOrWhiteSpace(filter.ResourceType)) query = query.Where(log => log.ResourceType == filter.ResourceType);
        if (!string.IsNullOrWhiteSpace(filter.Action)) query = query.Where(log => log.Action == filter.Action);
        if (filter.ResourceId is { } id) query = query.Where(log => log.ResourceId == id);
        if (filter.ActorUserId is { } userId) query = query.Where(log => log.ActorUserId == userId);
        if (filter.From is { } from) query = query.Where(log => log.OccurredAt >= from.ToUniversalTime());
        if (filter.To is { } to) query = query.Where(log => log.OccurredAt < to.ToUniversalTime());

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(log => log.OccurredAt).ThenBy(log => log.Id)
            .Skip(page.Offset).Take(page.PageSize)
            .Select(log => new AuditLogDto(log.Id, log.ActorUserId, log.ResourceType, log.ResourceId, log.Action,
                log.Reason, log.OccurredAt, log.Metadata)).ToListAsync(cancellationToken);
        return new PageResult<AuditLogDto>(items, page.Page, page.PageSize, total);
    }
}
