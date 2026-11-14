using FieldOps.Application.Common;
using FieldOps.Application.Operations;
using FieldOps.Application.WorkOrderDetails;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.WorkOrderDetails;
using FieldOps.Infrastructure.Operations;
using FieldOps.Infrastructure.Persistence;
using FieldOps.Infrastructure.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.WorkOrderDetails;

internal sealed class WorkOrderDetailService(FieldOpsDbContext database, OperationalWriteContext writes) : IWorkOrderDetailService
{
    public async Task<WorkOrderChecklistDto> GetChecklistAsync(Guid workOrderId, CancellationToken cancellationToken)
    {
        var order = await WorkOrderAccess.VisibleTo(database, writes.Actor).AsNoTracking().Where(order => order.Id == workOrderId)
            .Select(order => new
            {
                order.Id,
                order.Version,
                Items = database.WorkOrderChecklistItems.Where(item => item.TenantId == order.TenantId && item.WorkOrderId == order.Id)
                    .OrderBy(item => item.Position).ThenBy(item => item.Id)
                    .Select(item => new WorkOrderChecklistItemDto(item.Id, item.SourceTemplateId, item.SourceTemplateVersion,
                        item.SourceTemplateItemId, item.Label, item.Position, item.IsRequired, item.IsCompleted,
                        item.CompletedByUserId, item.CompletedAt)).ToList()
            }).SingleOrDefaultAsync(cancellationToken) ?? throw WorkOrderAccess.NotFound();
        var completed = order.Items.Count(item => item.IsCompleted);
        return new WorkOrderChecklistDto(order.Id, order.Version, order.Items.Count, completed,
            order.Items.Count == 0 ? 0 : completed * 100 / order.Items.Count,
            order.Items.All(item => !item.IsRequired || item.IsCompleted), order.Items);
    }

    public async Task<WorkOrderChecklistDto> ApplyChecklistAsync(Guid workOrderId, ApplyChecklistTemplateRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TemplateId == Guid.Empty)
            throw DetailRules.Invalid("invalid_checklist_template", "Provide a checklist template ID.");

        await using var transaction = await writes.BeginAsync(cancellationToken);
        var actor = writes.Actor;
        WorkOrderAccess.RequireManager(actor);
        var order = await WorkOrderAccess.LoadAsync(database, actor, workOrderId, cancellationToken);
        WorkOrderAccess.ApplyRule(() => order.Touch(request.ExpectedVersion, writes.Now));
        if (await OrderItems(actor.TenantId, workOrderId).AnyAsync(cancellationToken))
            throw DetailRules.Conflict("checklist_already_applied", "The work order already has a checklist snapshot.");

        var template = await database.ChecklistTemplates.AsNoTracking().SingleOrDefaultAsync(item =>
            item.TenantId == actor.TenantId && item.Id == request.TemplateId, cancellationToken)
            ?? throw DetailRules.Missing("checklist_template_not_found", "The checklist template was not found.");
        if (!template.IsActive)
            throw DetailRules.Conflict("checklist_template_archived", "An archived checklist template cannot be applied.");
        var sourceItems = await database.ChecklistTemplateItems.AsNoTracking().Where(item =>
            item.TenantId == actor.TenantId && item.TemplateId == template.Id && item.IsActive).ToListAsync(cancellationToken);
        if (sourceItems.Count == 0)
            throw DetailRules.Conflict("checklist_template_empty", "Add a checklist item before applying this template.");

        database.WorkOrderChecklistItems.AddRange(sourceItems.Select(item =>
            DetailRules.Apply(() => WorkOrderChecklistItem.Snapshot(order.Id, template, item))));
        writes.Audit("work_order", order.Id, "checklist_applied");
        await database.SaveChangesAsync(cancellationToken);
        var result = await GetChecklistAsync(workOrderId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<WorkOrderChecklistDto> UpdateChecklistItemAsync(Guid workOrderId, Guid itemId,
        UpdateWorkOrderChecklistItemRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await writes.BeginAsync(cancellationToken);
        var result = await UpdateInTransactionAsync(workOrderId, itemId, request, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    internal async Task<WorkOrderChecklistDto> UpdateInTransactionAsync(Guid workOrderId, Guid itemId,
        UpdateWorkOrderChecklistItemRequest request, CancellationToken cancellationToken)
    {
        if (request.IsCompleted is null)
            throw DetailRules.Invalid("checklist_completion_required", "Provide the item completion state.");

        var actor = writes.Actor;
        WorkOrderAccess.RequireExecution(actor);
        var order = await WorkOrderAccess.LoadAsync(database, actor, workOrderId, cancellationToken);
        var item = await OrderItems(actor.TenantId, order.Id).SingleOrDefaultAsync(item => item.Id == itemId, cancellationToken)
            ?? throw DetailRules.Missing("checklist_item_not_found", "The checklist item was not found on this work order.");

        var now = writes.Now;
        WorkOrderAccess.ApplyRule(() => order.Touch(request.ExpectedVersion, now));
        item.SetCompletion(request.IsCompleted.Value, actor.UserId, now);
        writes.Audit("work_order", order.Id, item.IsCompleted ? "checklist_item_completed" : "checklist_item_reopened");
        await database.SaveChangesAsync(cancellationToken);
        return await GetChecklistAsync(workOrderId, cancellationToken);
    }

    public async Task<PageResult<WorkOrderCommentDto>> ListCommentsAsync(Guid workOrderId, PageRequest page,
        CancellationToken cancellationToken)
    {
        page.Validate();
        var actor = writes.Actor;
        var orders = WorkOrderAccess.VisibleTo(database, actor).AsNoTracking().Where(order => order.Id == workOrderId);
        if (!await orders.AnyAsync(cancellationToken))
            throw WorkOrderAccess.NotFound();

        var includesInternal = CanUseInternalComments(actor.Role);
        // Include resource visibility in the data query too, including if an assignment changed since the existence check.
        var visibleComments = from comment in database.WorkOrderComments.AsNoTracking()
                              join order in orders on new { comment.TenantId, Id = comment.WorkOrderId } equals new { order.TenantId, order.Id }
                              where includesInternal || comment.Visibility == CommentVisibility.TechnicianVisible
                              select comment;
        var count = await visibleComments.CountAsync(cancellationToken);
        var comments = await (from comment in visibleComments
                              join user in database.Users.AsNoTracking() on comment.AuthorUserId equals user.Id
                              orderby comment.CreatedAt descending, comment.Id descending
                              select new WorkOrderCommentDto(comment.Id, comment.AuthorUserId, user.DisplayName, comment.Body,
                                  comment.Visibility, comment.CreatedAt))
            .Skip(page.Offset).Take(page.PageSize).ToListAsync(cancellationToken);
        return new PageResult<WorkOrderCommentDto>(comments, page.Page, page.PageSize, count);
    }

    public async Task<AddedWorkOrderCommentDto> AddCommentAsync(Guid workOrderId, AddWorkOrderCommentRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await writes.BeginAsync(cancellationToken);
        var result = await AddCommentInTransactionAsync(workOrderId, request, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    internal async Task<AddedWorkOrderCommentDto> AddCommentInTransactionAsync(Guid workOrderId,
        AddWorkOrderCommentRequest request, CancellationToken cancellationToken)
    {
        var actor = writes.Actor;
        WorkOrderAccess.RequireExecution(actor);
        var order = await WorkOrderAccess.LoadAsync(database, actor, workOrderId, cancellationToken);
        if (request.Visibility == CommentVisibility.Internal && !CanUseInternalComments(actor.Role))
            throw new FieldOpsException(ErrorKind.Forbidden, "internal_comment_forbidden", "Only an owner or manager may add internal comments.");

        var now = writes.Now;
        var comment = DetailRules.Apply(() => WorkOrderComment.Create(actor.TenantId, order.Id, actor.UserId,
            request.Body, request.Visibility, now));
        WorkOrderAccess.ApplyRule(() => order.Touch(request.ExpectedVersion, now));
        var displayName = await database.Users.AsNoTracking().Where(user => user.Id == actor.UserId)
            .Select(user => user.DisplayName).SingleAsync(cancellationToken);
        database.WorkOrderComments.Add(comment);
        writes.Audit("work_order", order.Id, "comment_added");
        await database.SaveChangesAsync(cancellationToken);
        return new AddedWorkOrderCommentDto(new WorkOrderCommentDto(comment.Id, comment.AuthorUserId, displayName, comment.Body,
            comment.Visibility, comment.CreatedAt), order.Version);
    }

    private IQueryable<WorkOrderChecklistItem> OrderItems(Guid tenantId, Guid workOrderId) =>
        database.WorkOrderChecklistItems.Where(item => item.TenantId == tenantId && item.WorkOrderId == workOrderId);

    private static bool CanUseInternalComments(OrganizationRole role) =>
        role is OrganizationRole.OrganizationOwner or OrganizationRole.Manager;

}
