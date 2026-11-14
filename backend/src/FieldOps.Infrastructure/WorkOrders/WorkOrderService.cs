using System.Globalization;
using System.Linq.Expressions;
using FieldOps.Application.Common;
using FieldOps.Application.Operations;
using FieldOps.Application.WorkOrders;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.WorkOrders;
using FieldOps.Infrastructure.Operations;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.WorkOrders;

internal sealed class WorkOrderService(FieldOpsDbContext database, OperationalWriteContext writes) : IWorkOrderService
{
    private const string ResourceType = "work_order";
    private static readonly Expression<Func<WorkOrder, WorkOrderDto>> Projection = order => new WorkOrderDto(
        order.Id, order.WorkOrderNumber, order.CustomerId, order.ServiceLocationId, order.AssetId, order.Title,
        order.Description, order.Priority, order.Status, order.PlannedStartAt, order.PlannedEndAt, order.ActualStartAt,
        order.ActualEndAt, order.CreatedByUserId, order.CreatedAt, order.UpdatedAt, order.Version);
    internal static readonly Func<WorkOrder, WorkOrderDto> ToDto = Projection.Compile();

    public async Task<PageResult<WorkOrderDto>> ListAsync(WorkOrderListQuery request, CancellationToken cancellationToken)
    {
        var actor = writes.Actor;
        var page = new PageRequest(request.Page ?? 1, request.PageSize ?? 25);
        page.Validate();
        var from = request.PlannedFrom is null ? (DateTimeOffset?)null : ParseInstant(request.PlannedFrom);
        var to = request.PlannedTo is null ? (DateTimeOffset?)null : ParseInstant(request.PlannedTo);
        if (from >= to) throw Invalid("invalid_schedule_filter", "The planned filter end must be after the start.");
        if (request.Status is { } status && !Enum.IsDefined(status) || request.Priority is { } priority && !Enum.IsDefined(priority))
            throw Invalid("invalid_work_order_filter", "Provide a valid status and priority.");

        var query = WorkOrderAccess.VisibleTo(database, actor).AsNoTracking();
        if (request.CustomerId is { } customerId) query = query.Where(order => order.CustomerId == customerId);
        if (request.ServiceLocationId is { } locationId) query = query.Where(order => order.ServiceLocationId == locationId);
        if (request.AssetId is { } assetId) query = query.Where(order => order.AssetId == assetId);
        if (request.Status is { } statusFilter) query = query.Where(order => order.Status == statusFilter);
        if (request.Priority is { } priorityFilter) query = query.Where(order => order.Priority == priorityFilter);
        if (from is not null) query = query.Where(order => order.PlannedEndAt > from);
        if (to is not null) query = query.Where(order => order.PlannedStartAt < to);
        if (request.TechnicianUserId is { } technician)
            query = query.Where(order => database.WorkOrderAssignments.Any(assignment =>
                assignment.TenantId == actor.TenantId && assignment.WorkOrderId == order.Id
                && assignment.TechnicianUserId == technician && assignment.RemovedAt == null));
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            if (request.Search.Length > 100 || request.Search.Any(char.IsControl))
                throw Invalid("invalid_search", "Use at most 100 characters without control characters for search.");
            var pattern = "%" + request.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(order => EF.Functions.ILike(order.Title, pattern, "\\") || EF.Functions.ILike(order.WorkOrderNumber, pattern, "\\"));
        }

        var sorted = Sort(query, request.SortBy, request.Descending ?? true);
        var count = await query.CountAsync(cancellationToken);
        var items = await sorted.ThenBy(order => order.Id).Skip(page.Offset).Take(page.PageSize).Select(Projection).ToListAsync(cancellationToken);
        return new PageResult<WorkOrderDto>(items, page.Page, page.PageSize, count);
    }

    public async Task<WorkOrderDetailDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = writes.Actor;
        var order = await WorkOrderAccess.VisibleTo(database, actor).AsNoTracking().Where(order => order.Id == id)
            .Select(Projection).SingleOrDefaultAsync(cancellationToken) ?? throw WorkOrderAccess.NotFound();
        var customer = await database.Customers.Where(customer => customer.TenantId == actor.TenantId && customer.Id == order.CustomerId)
            .Select(customer => new WorkOrderCustomerDto(customer.Id, customer.Name)).SingleAsync(cancellationToken);
        var location = await database.ServiceLocations.Where(location => location.TenantId == actor.TenantId && location.Id == order.ServiceLocationId)
            .Select(location => new WorkOrderLocationDto(location.Id, location.Name, location.AddressLine, location.City, location.StateOrProvince,
                location.Country, location.PostalCode, location.Latitude, location.Longitude, location.ContactName, location.ContactPhone))
            .SingleAsync(cancellationToken);
        var asset = order.AssetId is { } assetId
            ? await database.Assets.Where(asset => asset.TenantId == actor.TenantId && asset.Id == assetId)
                .Select(asset => new WorkOrderAssetDto(asset.Id, asset.AssetCode, asset.Name, asset.Brand, asset.Model, asset.SerialNumber))
                .SingleAsync(cancellationToken)
            : null;
        var assignments = await (from assignment in ActiveAssignments(id, actor.TenantId)
                                 join user in database.Users on assignment.TechnicianUserId equals user.Id
                                 orderby assignment.IsPrimary descending, assignment.AssignedAt, assignment.Id
                                 select new WorkOrderAssignmentDto(assignment.Id, user.Id, user.DisplayName,
                                     assignment.IsPrimary, assignment.AssignedByUserId, assignment.AssignedAt))
            .ToListAsync(cancellationToken);
        return new WorkOrderDetailDto(order, customer, location, asset, assignments);
    }

    public async Task<PageResult<WorkOrderHistoryDto>> HistoryAsync(Guid id, PageRequest page, CancellationToken cancellationToken)
    {
        page.Validate();
        var actor = writes.Actor;
        if (!await WorkOrderAccess.VisibleTo(database, actor).AnyAsync(order => order.Id == id, cancellationToken))
            throw WorkOrderAccess.NotFound();
        var canReadReason = actor.Role is OrganizationRole.OrganizationOwner or OrganizationRole.Manager;
        var query = from entry in database.WorkOrderStatusHistory.AsNoTracking()
                    join user in database.Users on entry.ChangedByUserId equals user.Id
                    where entry.TenantId == actor.TenantId && entry.WorkOrderId == id
                    orderby entry.WorkOrderVersion
                    select new WorkOrderHistoryDto(entry.Id, entry.PreviousStatus, entry.Status, user.Id,
                        user.DisplayName, entry.ChangedAt, entry.WorkOrderVersion, canReadReason ? entry.Reason : null);
        var count = await query.CountAsync(cancellationToken);
        var items = await query.Skip(page.Offset).Take(page.PageSize).ToListAsync(cancellationToken);
        return new PageResult<WorkOrderHistoryDto>(items, page.Page, page.PageSize, count);
    }

    public async Task<WorkOrderDto> CreateAsync(CreateWorkOrderRequest request, CancellationToken cancellationToken)
    {
        var actor = writes.Actor;
        WorkOrderAccess.RequireManager(actor);
        await using var transaction = await writes.BeginAsync(cancellationToken);
        var now = writes.Now;
        WorkOrder order;
        try
        {
            order = WorkOrder.Create(actor.TenantId, actor.UserId, request.CustomerId, request.ServiceLocationId,
                request.AssetId, request.Title, request.Description, request.Priority, now);
        }
        catch (WorkOrderRuleException exception)
        {
            throw WorkOrderAccess.RuleError(exception);
        }
        await RequireCatalogReferencesAsync(order, cancellationToken);
        database.WorkOrders.Add(order);
        RecordStatus(order, null, actor.UserId, now, null);
        writes.Audit(ResourceType, order.Id, "created");
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(order);
    }

    public async Task<WorkOrderDto> UpdateAsync(Guid id, UpdateWorkOrderRequest request, CancellationToken cancellationToken)
    {
        var actor = writes.Actor;
        WorkOrderAccess.RequireManager(actor);
        if (request.ClearAsset && request.AssetId is not null)
            throw Invalid("invalid_work_order_reference", "Choose either an asset ID or clearing the asset.");
        if (request is { Title: null, Description: null, Priority: null, CustomerId: null, ServiceLocationId: null, AssetId: null, ClearAsset: false })
            throw Invalid("empty_work_order_update", "Provide at least one work order field to update.");

        await using var transaction = await writes.BeginAsync(cancellationToken);
        var order = await WorkOrderAccess.LoadAsync(database, actor, id, cancellationToken);
        var customerId = request.CustomerId ?? order.CustomerId;
        var locationId = request.ServiceLocationId ?? order.ServiceLocationId;
        var assetId = request.ClearAsset ? null : request.AssetId ?? order.AssetId;
        var referencesChanged = customerId != order.CustomerId || locationId != order.ServiceLocationId || assetId != order.AssetId;
        WorkOrderAccess.ApplyRule(() => order.Update(request.ExpectedVersion, customerId, locationId, assetId,
            request.Title ?? order.Title, request.Description ?? order.Description, request.Priority ?? order.Priority, writes.Now));
        if (referencesChanged) await RequireCatalogReferencesAsync(order, cancellationToken);
        writes.Audit(ResourceType, id, "updated");
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(order);
    }

    public async Task<WorkOrderDto> ScheduleAsync(Guid id, ScheduleWorkOrderRequest request, CancellationToken cancellationToken)
    {
        var actor = writes.Actor;
        WorkOrderAccess.RequireManager(actor);
        var start = ParseInstant(request.PlannedStartAt);
        var end = ParseInstant(request.PlannedEndAt);
        var reason = NormalizeReason(request.OverrideReason);
        await using var transaction = await writes.BeginAsync(cancellationToken);
        var order = await WorkOrderAccess.LoadAsync(database, actor, id, cancellationToken);
        var before = await WorkOrderAuditSnapshot.ReadAsync(database, actor.TenantId, id, cancellationToken);
        var previous = order.Status;
        var now = writes.Now;
        WorkOrderAccess.ApplyRule(() => order.Schedule(request.ExpectedVersion, start, end, now));
        await RequireCatalogReferencesAsync(order, cancellationToken);
        var technicians = await ActiveAssignments(id, actor.TenantId).Select(assignment => assignment.TechnicianUserId)
            .ToArrayAsync(cancellationToken);
        var overridden = await CheckAvailabilityAsync(order, technicians, start, end, reason, cancellationToken);
        if (order.Status != previous) RecordStatus(order, previous, actor.UserId, now, null);
        await database.SaveChangesAsync(cancellationToken);
        var after = await WorkOrderAuditSnapshot.ReadAsync(database, actor.TenantId, id, cancellationToken);
        var metadata = before.ChangesTo(after);
        writes.Audit(ResourceType, id, previous == WorkOrderStatus.Draft ? "scheduled" : "rescheduled",
            metadata: metadata, occurredAt: now);
        if (overridden) writes.Audit(ResourceType, id, "availability_overridden", reason, metadata, now);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(order);
    }

    public async Task<WorkOrderDto> AssignAsync(Guid id, AssignWorkOrderRequest request, CancellationToken cancellationToken)
    {
        var actor = writes.Actor;
        WorkOrderAccess.RequireManager(actor);
        var technicians = request.TechnicianUserIds;
        if (technicians is null || technicians.Count is < 1 or > 20 || technicians.Contains(Guid.Empty) ||
            technicians.Distinct().Count() != technicians.Count || !technicians.Contains(request.PrimaryTechnicianUserId))
            throw Invalid("invalid_work_order_assignments", "Provide 1 to 20 distinct technicians and choose one of them as primary.");
        var reason = NormalizeReason(request.OverrideReason);
        await using var transaction = await writes.BeginAsync(cancellationToken);
        var order = await WorkOrderAccess.LoadAsync(database, actor, id, cancellationToken);
        var before = await WorkOrderAuditSnapshot.ReadAsync(database, actor.TenantId, id, cancellationToken);
        var previous = order.Status;
        var now = writes.Now;
        WorkOrderAccess.ApplyRule(() => order.Assign(request.ExpectedVersion, now));
        var technicianCount = await (from membership in database.OrganizationMemberships
                                     join user in database.Users on membership.UserId equals user.Id
                                     where membership.TenantId == actor.TenantId && membership.IsActive
                                         && membership.Role == OrganizationRole.Technician && technicians.Contains(membership.UserId)
                                         && user.IsActive && user.EmailConfirmed && user.PasswordHash != null && user.PasswordHash != ""
                                     select membership.Id).CountAsync(cancellationToken);
        if (technicianCount != technicians.Count)
            throw Invalid("invalid_technician", "Every assignee must be an active technician in the current organization.");
        var overridden = await CheckAvailabilityAsync(order, technicians, order.PlannedStartAt!.Value,
            order.PlannedEndAt!.Value, reason, cancellationToken);
        var active = await ActiveAssignments(id, actor.TenantId).ToListAsync(cancellationToken);
        var retained = new HashSet<Guid>();
        foreach (var assignment in active)
        {
            if (technicians.Contains(assignment.TechnicianUserId) &&
                assignment.IsPrimary == (assignment.TechnicianUserId == request.PrimaryTechnicianUserId))
                retained.Add(assignment.TechnicianUserId);
            else
                assignment.Retire(now);
        }
        // Release any filtered unique keys before inserting replacements. Both saves share this transaction.
        if (active.Count != retained.Count) await database.SaveChangesAsync(cancellationToken);
        foreach (var technician in technicians.Where(technician => !retained.Contains(technician)))
            database.WorkOrderAssignments.Add(WorkOrderAssignment.Create(order, technician,
                technician == request.PrimaryTechnicianUserId, actor.UserId, now));
        if (order.Status != previous) RecordStatus(order, previous, actor.UserId, now, null);
        await database.SaveChangesAsync(cancellationToken);
        var after = await WorkOrderAuditSnapshot.ReadAsync(database, actor.TenantId, id, cancellationToken);
        var metadata = before.ChangesTo(after);
        writes.Audit(ResourceType, id, "assignments_changed", metadata: metadata, occurredAt: now);
        if (overridden) writes.Audit(ResourceType, id, "availability_overridden", reason, metadata, now);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(order);
    }

    public async Task<WorkOrderDto> ExecuteAsync(Guid id, WorkOrderAction action, WorkOrderActionRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await writes.BeginAsync(cancellationToken);
        var result = await ExecuteInTransactionAsync(id, action, request, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    internal async Task<WorkOrderDto> ExecuteInTransactionAsync(Guid id, WorkOrderAction action,
        WorkOrderActionRequest request, CancellationToken cancellationToken)
    {
        var actor = writes.Actor;
        if (action == WorkOrderAction.Cancel) WorkOrderAccess.RequireManager(actor);
        else WorkOrderAccess.RequireExecution(actor);
        var reason = NormalizeReason(request.Reason);
        var order = await WorkOrderAccess.LoadAsync(database, actor, id, cancellationToken);
        var previous = order.Status;
        var now = writes.Now;
        var incomplete = action == WorkOrderAction.Complete && await database.WorkOrderChecklistItems.AnyAsync(item =>
            item.TenantId == actor.TenantId && item.WorkOrderId == id && item.IsRequired && !item.IsCompleted, cancellationToken);
        WorkOrderAccess.ApplyRule(() => order.Execute(action, request.ExpectedVersion, now, incomplete));
        RecordStatus(order, previous, actor.UserId, now, reason);
        writes.Audit(ResourceType, id, action.ToString().ToLowerInvariant(), reason);
        await database.SaveChangesAsync(cancellationToken);
        return ToDto(order);
    }

    private IQueryable<WorkOrderAssignment> ActiveAssignments(Guid workOrderId, Guid tenantId) =>
        database.WorkOrderAssignments.Where(assignment => assignment.TenantId == tenantId
            && assignment.WorkOrderId == workOrderId && assignment.RemovedAt == null);

    private async Task RequireCatalogReferencesAsync(WorkOrder order, CancellationToken cancellationToken)
    {
        var tenant = order.TenantId;
        if (!await database.Customers.AnyAsync(customer => customer.TenantId == tenant && customer.Id == order.CustomerId && customer.IsActive,
                cancellationToken)
            || !await database.ServiceLocations.AnyAsync(location => location.TenantId == tenant && location.CustomerId == order.CustomerId
                && location.Id == order.ServiceLocationId && location.IsActive, cancellationToken)
            || order.AssetId is { } assetId && !await database.Assets.AnyAsync(asset => asset.TenantId == tenant
                && asset.CustomerId == order.CustomerId && asset.ServiceLocationId == order.ServiceLocationId
                && asset.Id == assetId && asset.IsActive, cancellationToken))
            throw Invalid("invalid_work_order_reference", "Provide an active customer, service location and matching asset in the current organization.");
    }

    private async Task<bool> CheckAvailabilityAsync(WorkOrder order, IReadOnlyList<Guid> technicianIds, DateTimeOffset start,
        DateTimeOffset end, string? reason, CancellationToken cancellationToken)
    {
        if (technicianIds.Count == 0) return false;
        var overlaps = await (from assignment in database.WorkOrderAssignments
                              join other in database.WorkOrders
                                  on new { assignment.TenantId, Id = assignment.WorkOrderId } equals new { other.TenantId, other.Id }
                              where assignment.TenantId == order.TenantId && assignment.RemovedAt == null
                                  && other.Id != order.Id && technicianIds.Contains(assignment.TechnicianUserId)
                                  && other.Status != WorkOrderStatus.Completed && other.Status != WorkOrderStatus.Cancelled
                                  && other.PlannedStartAt < end && start < other.PlannedEndAt
                              select other.Id).AnyAsync(cancellationToken);
        if (overlaps && reason is null)
            throw new FieldOpsException(ErrorKind.Conflict, "technician_schedule_conflict",
                "A technician already has a conflicting assignment. Provide a reason to override.");
        return overlaps;
    }

    private void RecordStatus(WorkOrder order, WorkOrderStatus? previous, Guid userId, DateTimeOffset now, string? reason) =>
        database.WorkOrderStatusHistory.Add(WorkOrderStatusHistory.Create(order, previous, userId, now, reason));

    private static DateTimeOffset ParseInstant(string value)
    {
        if (value is null || !(value.EndsWith('Z') || value.Length >= 6 && value[^6] is '+' or '-')
            || !DateTimeOffset.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
            throw Invalid("invalid_schedule_instant", "Provide an ISO 8601 timestamp with Z or an explicit UTC offset.");
        // PostgreSQL stores timestamptz at microsecond precision. Reject finer precision
        // before interval validation can accept endpoints that the database rounds together.
        if (instant.Ticks % 10 != 0)
            throw Invalid("invalid_schedule_instant", "Use no more than six fractional second digits.");
        return instant.ToUniversalTime();
    }

    private static string? NormalizeReason(string? value)
    {
        if (value is null) return null;
        if (value.Length > 500 || value.Any(char.IsControl))
            throw Invalid("invalid_work_order_reason", "Use a reason of at most 500 characters without control characters.");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IOrderedQueryable<WorkOrder> Sort(IQueryable<WorkOrder> query, string? field, bool descending) =>
        (field?.ToLowerInvariant() ?? "createdat", descending) switch
        {
            ("createdat", true) => query.OrderByDescending(order => order.CreatedAt),
            ("createdat", false) => query.OrderBy(order => order.CreatedAt),
            ("updatedat", true) => query.OrderByDescending(order => order.UpdatedAt),
            ("updatedat", false) => query.OrderBy(order => order.UpdatedAt),
            ("title", true) => query.OrderByDescending(order => order.Title),
            ("title", false) => query.OrderBy(order => order.Title),
            ("plannedstartat", true) => query.OrderByDescending(order => order.PlannedStartAt),
            ("plannedstartat", false) => query.OrderBy(order => order.PlannedStartAt),
            _ => throw Invalid("invalid_work_order_sort", "Sort by createdAt, updatedAt, title or plannedStartAt.")
        };

    private static FieldOpsException Invalid(string code, string message) => new(ErrorKind.Validation, code, message);
}
