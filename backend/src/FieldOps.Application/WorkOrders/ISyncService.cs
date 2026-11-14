using FieldOps.Application.WorkOrderDetails;

namespace FieldOps.Application.WorkOrders;

public sealed record SyncOperation(Guid Id, Guid OrderId, long ExpectedVersion, string Kind, DateTimeOffset CreatedAt,
    Guid? ItemId = null, bool? IsCompleted = null, string? Body = null);
public sealed record SyncPushRequest(int Protocol, IReadOnlyList<SyncOperation> Operations);
public sealed record SyncOperationResult(Guid Id, string Status, long? Version = null, string? Code = null, bool Duplicate = false);
public sealed record SyncPushResponse(IReadOnlyList<SyncOperationResult> Results);
public sealed record SyncOrder(WorkOrderDto Order, WorkOrderChecklistDto Checklist, IReadOnlyList<WorkOrderCommentDto> Notes);
public sealed record SyncPullResponse(string Cursor, bool HasMore, DateTimeOffset ServerTime, IReadOnlyList<SyncOrder> Orders);

public interface ISyncService
{
    Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken);
    Task<SyncPullResponse> PullAsync(int protocol, string? cursor, CancellationToken cancellationToken);
}
