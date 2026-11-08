using System.Data;
using FieldOps.Application.Common;
using FieldOps.Application.Operations;
using FieldOps.Application.Reporting;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.WorkOrders;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Operations;

internal sealed class ReportingReader(FieldOpsDbContext database, OperationalWriteContext operations) : IReportingReader
{
    private Guid TenantId
    {
        get
        {
            var actor = operations.Actor;
            if (actor.Role is not (OrganizationRole.OrganizationOwner or OrganizationRole.Manager))
                throw new FieldOpsException(ErrorKind.Forbidden, "reports_forbidden", "Reports require organization administration access.");
            return actor.TenantId;
        }
    }

    public async Task<PageResult<TechnicianDto>> TechniciansAsync(TechnicianFilter filter, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var page = new PageRequest(filter.Page ?? 1, filter.PageSize ?? 25);
        page.Validate();
        if (filter.Search is { Length: > 100 } || filter.Search?.Any(char.IsControl) == true)
            throw new FieldOpsException(ErrorKind.Validation, "invalid_search", "Search must contain at most 100 printable characters.");
        var query = from membership in database.OrganizationMemberships.AsNoTracking()
                    join user in database.Users.AsNoTracking() on membership.UserId equals user.Id
                    where membership.TenantId == tenantId && membership.IsActive && membership.Role == OrganizationRole.Technician
                        && user.IsActive && user.EmailConfirmed
                    select new { UserId = user.Id, user.DisplayName };
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToUpperInvariant();
            query = query.Where(user => user.DisplayName.ToUpper().Contains(search));
        }
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(user => user.DisplayName).ThenBy(user => user.UserId)
            .Skip(page.Offset).Take(page.PageSize).ToListAsync(cancellationToken);
        return new PageResult<TechnicianDto>(items.Select(user => new TechnicianDto(user.UserId, user.DisplayName)).ToList(), page.Page, page.PageSize, count);
    }

    public async Task<OperationsReportDto> ReportAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        if (filter.From >= filter.To)
            throw new FieldOpsException(ErrorKind.Validation, "invalid_report_range", "The end must be after the start.");

        // One consistent database snapshot for all counters, charts and activity lists.
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var now = operations.Now;
        var orders = database.WorkOrders.AsNoTracking().Where(order => order.TenantId == tenantId);
        if (filter.From is { } from) orders = orders.Where(order => order.CreatedAt >= from.ToUniversalTime());
        if (filter.To is { } to) orders = orders.Where(order => order.CreatedAt < to.ToUniversalTime());
        var total = await orders.CountAsync(cancellationToken);
        var completed = await orders.CountAsync(order => order.Status == WorkOrderStatus.Completed, cancellationToken);
        var active = orders.Where(order => order.Status != WorkOrderStatus.Completed && order.Status != WorkOrderStatus.Cancelled);
        var activeCount = await active.CountAsync(cancellationToken);
        var overdue = await active.CountAsync(order => order.PlannedEndAt < now, cancellationToken);
        var statuses = await orders.GroupBy(order => order.Status)
            .Select(group => new { Name = group.Key, Count = group.Count() }).ToListAsync(cancellationToken);
        var priorities = await orders.GroupBy(order => order.Priority)
            .Select(group => new { Name = group.Key, Count = group.Count() }).ToListAsync(cancellationToken);
        var technicians = from member in database.OrganizationMemberships.AsNoTracking()
                          join user in database.Users.AsNoTracking() on member.UserId equals user.Id
                          where member.TenantId == tenantId && member.IsActive && member.Role == OrganizationRole.Technician
                              && user.IsActive && user.EmailConfirmed
                          select new { user.Id, user.DisplayName };
        var technicianCount = await technicians.CountAsync(cancellationToken);
        var workload = await technicians.Select(user => new
        {
            UserId = user.Id,
            user.DisplayName,
            ActiveOrders = database.WorkOrderAssignments.Count(assignment => assignment.TenantId == tenantId && assignment.RemovedAt == null
                && assignment.TechnicianUserId == user.Id && active.Any(order => order.Id == assignment.WorkOrderId))
        })
            .OrderByDescending(user => user.ActiveOrders).ThenBy(user => user.DisplayName).ThenBy(user => user.UserId)
            .Take(50).ToListAsync(cancellationToken);
        var upcoming = await active.Where(order => order.PlannedStartAt >= now).OrderBy(order => order.PlannedStartAt).ThenBy(order => order.Id)
            .Take(8).Select(order => new UpcomingOrderDto(order.Id, order.WorkOrderNumber, order.Title, order.Status.ToString(), order.PlannedStartAt))
            .ToListAsync(cancellationToken);
        var activities = database.AuditLogs.AsNoTracking().Where(log => log.TenantId == tenantId);
        if (filter.From is { } activityFrom) activities = activities.Where(log => log.OccurredAt >= activityFrom.ToUniversalTime());
        if (filter.To is { } activityTo) activities = activities.Where(log => log.OccurredAt < activityTo.ToUniversalTime());
        var recent = await activities.OrderByDescending(log => log.OccurredAt).ThenBy(log => log.Id).Take(8)
            .Select(log => new ActivityDto(log.Id, log.ResourceType, log.ResourceId, log.Action, log.OccurredAt)).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OperationsReportDto(total, activeCount, completed, overdue, technicianCount,
            total == 0 ? 0 : Math.Round(100m * completed / total, 1),
            statuses.Select(value => new DistributionDto(value.Name.ToString(), value.Count)).ToList(),
            priorities.Select(value => new DistributionDto(value.Name.ToString(), value.Count)).ToList(),
            workload.Select(user => new WorkloadDto(user.UserId, user.DisplayName, user.ActiveOrders)).ToList(), upcoming, recent);
    }
}
