using FieldOps.Application.Operations;
using FieldOps.Domain.WorkOrderDetails;

namespace FieldOps.Application.WorkOrderDetails;

public sealed record CreateChecklistTemplateRequest(string Name);
public sealed record UpdateChecklistTemplateRequest(string Name, long ExpectedVersion);
public sealed record CreateChecklistTemplateItemRequest(string Label, int Position, bool IsRequired, long ExpectedVersion);
public sealed record UpdateChecklistTemplateItemRequest(string? Label, int? Position, bool? IsRequired, long ExpectedVersion);
public sealed record ApplyChecklistTemplateRequest(Guid TemplateId, long ExpectedVersion);
public sealed record UpdateWorkOrderChecklistItemRequest(bool? IsCompleted, long ExpectedVersion);
public sealed record AddWorkOrderCommentRequest(string Body, CommentVisibility Visibility, long ExpectedVersion);

public sealed record ChecklistTemplateSummary(Guid Id, string Name, bool IsActive, long Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ChecklistTemplateItemDto(Guid Id, string Label, int Position, bool IsRequired);
public sealed record ChecklistTemplateDto(Guid Id, string Name, bool IsActive, long Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<ChecklistTemplateItemDto> Items);
public sealed record WorkOrderChecklistItemDto(Guid Id, Guid SourceTemplateId, long SourceTemplateVersion,
    Guid SourceTemplateItemId, string Label, int Position, bool IsRequired, bool IsCompleted,
    Guid? CompletedByUserId, DateTimeOffset? CompletedAt);
public sealed record WorkOrderChecklistDto(Guid WorkOrderId, long WorkOrderVersion, int TotalItems, int CompletedItems,
    int ProgressPercent, bool RequiredItemsComplete, IReadOnlyList<WorkOrderChecklistItemDto> Items);
public sealed record WorkOrderCommentDto(Guid Id, Guid AuthorUserId, string AuthorDisplayName, string Body,
    CommentVisibility Visibility, DateTimeOffset CreatedAt);
public sealed record AddedWorkOrderCommentDto(WorkOrderCommentDto Comment, long WorkOrderVersion);

public interface IChecklistTemplateService
{
    Task<PageResult<ChecklistTemplateSummary>> ListAsync(PageRequest page, string? search, bool? isActive, CancellationToken cancellationToken);
    Task<ChecklistTemplateDto> GetAsync(Guid templateId, CancellationToken cancellationToken);
    Task<ChecklistTemplateDto> CreateAsync(CreateChecklistTemplateRequest request, CancellationToken cancellationToken);
    Task<ChecklistTemplateDto> UpdateAsync(Guid templateId, UpdateChecklistTemplateRequest request, CancellationToken cancellationToken);
    Task ArchiveAsync(Guid templateId, long expectedVersion, CancellationToken cancellationToken);
    Task<ChecklistTemplateDto> AddItemAsync(Guid templateId, CreateChecklistTemplateItemRequest request, CancellationToken cancellationToken);
    Task<ChecklistTemplateDto> UpdateItemAsync(Guid templateId, Guid itemId, UpdateChecklistTemplateItemRequest request, CancellationToken cancellationToken);
    Task<ChecklistTemplateDto> ArchiveItemAsync(Guid templateId, Guid itemId, long expectedVersion, CancellationToken cancellationToken);
}

public interface IWorkOrderDetailService
{
    Task<WorkOrderChecklistDto> GetChecklistAsync(Guid workOrderId, CancellationToken cancellationToken);
    Task<WorkOrderChecklistDto> ApplyChecklistAsync(Guid workOrderId, ApplyChecklistTemplateRequest request, CancellationToken cancellationToken);
    Task<WorkOrderChecklistDto> UpdateChecklistItemAsync(Guid workOrderId, Guid itemId, UpdateWorkOrderChecklistItemRequest request,
        CancellationToken cancellationToken);
    Task<PageResult<WorkOrderCommentDto>> ListCommentsAsync(Guid workOrderId, PageRequest page, CancellationToken cancellationToken);
    Task<AddedWorkOrderCommentDto> AddCommentAsync(Guid workOrderId, AddWorkOrderCommentRequest request, CancellationToken cancellationToken);
}
