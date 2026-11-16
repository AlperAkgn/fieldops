using FieldOps.Application.Operations;
using FieldOps.Application.Tenancy;

namespace FieldOps.Application.Collaboration;

public sealed record OrderChanged(Guid EventId, Guid? WorkOrderId, long Version, string Kind);

public interface IOrderChangeDelivery
{
    Task DeliverAsync(Guid tenantId, OrderChanged change, IReadOnlyCollection<Guid> formerAssignees, CancellationToken cancellationToken);
}

public interface ICollaborationAccess
{
    Task<TenantActor?> ValidateAsync(TenantActor actor, CancellationToken cancellationToken);
    Task<bool> CanReadOrderAsync(TenantActor actor, Guid orderId, CancellationToken cancellationToken);
}

public sealed record NotificationDto(Guid Id, Guid? WorkOrderId, string Kind, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
public sealed record RegisterPushDevice(string Token);

public interface INotifications
{
    Task<PageResult<NotificationDto>> ListAsync(PageRequest page, CancellationToken cancellationToken);
    Task MarkReadAsync(Guid id, CancellationToken cancellationToken);
    Task RegisterAsync(Guid deviceId, RegisterPushDevice request, CancellationToken cancellationToken);
    Task RevokeAsync(Guid deviceId, CancellationToken cancellationToken);
}

public interface ICollaborationDispatcher
{
    Task DispatchAsync(CancellationToken cancellationToken);
}
