using FieldOps.Application.Operations;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.WorkOrders;

public sealed record CreateWorkOrderRequest(Guid CustomerId, Guid ServiceLocationId, Guid? AssetId, string Title,
    string? Description, WorkOrderPriority Priority);

public sealed record UpdateWorkOrderRequest(long ExpectedVersion, string? Title, string? Description, WorkOrderPriority? Priority,
    Guid? CustomerId = null, Guid? ServiceLocationId = null, Guid? AssetId = null, bool ClearAsset = false);

public sealed record ScheduleWorkOrderRequest(long ExpectedVersion, string PlannedStartAt, string PlannedEndAt, string? OverrideReason = null);

public sealed record AssignWorkOrderRequest(long ExpectedVersion, IReadOnlyList<Guid> TechnicianUserIds, Guid PrimaryTechnicianUserId,
    string? OverrideReason = null);

public sealed record WorkOrderActionRequest(long ExpectedVersion, string? Reason = null);

public sealed record WorkOrderListQuery
{
    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public bool? Descending { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? ServiceLocationId { get; init; }
    public Guid? AssetId { get; init; }
    public Guid? TechnicianUserId { get; init; }
    public WorkOrderStatus? Status { get; init; }
    public WorkOrderPriority? Priority { get; init; }
    public string? PlannedFrom { get; init; }
    public string? PlannedTo { get; init; }
}

public sealed record WorkOrderDto(Guid Id, string WorkOrderNumber, Guid CustomerId, Guid ServiceLocationId, Guid? AssetId,
    string Title, string? Description, WorkOrderPriority Priority, WorkOrderStatus Status, DateTimeOffset? PlannedStartAt,
    DateTimeOffset? PlannedEndAt, DateTimeOffset? ActualStartAt, DateTimeOffset? ActualEndAt, Guid CreatedByUserId,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version);

public sealed record WorkOrderCustomerDto(Guid Id, string Name);

public sealed record WorkOrderLocationDto(Guid Id, string Name, string AddressLine, string City, string? StateOrProvince,
    string Country, string? PostalCode, decimal? Latitude, decimal? Longitude, string? ContactName, string? ContactPhone);

public sealed record WorkOrderAssetDto(Guid Id, string AssetCode, string Name, string? Brand, string? Model, string? SerialNumber);

public sealed record WorkOrderAssignmentDto(Guid Id, Guid TechnicianUserId, string DisplayName, bool IsPrimary,
    Guid AssignedByUserId, DateTimeOffset AssignedAt);

public sealed record WorkOrderDetailDto(WorkOrderDto Order, WorkOrderCustomerDto Customer, WorkOrderLocationDto ServiceLocation,
    WorkOrderAssetDto? Asset, IReadOnlyList<WorkOrderAssignmentDto> Assignments);

public sealed record WorkOrderHistoryDto(Guid Id, WorkOrderStatus? PreviousStatus, WorkOrderStatus Status,
    Guid ChangedByUserId, string DisplayName, DateTimeOffset ChangedAt, long Version, string? Reason);

public interface IWorkOrderService
{
    Task<PageResult<WorkOrderDto>> ListAsync(WorkOrderListQuery request, CancellationToken cancellationToken);
    Task<WorkOrderDetailDto> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<WorkOrderHistoryDto>> HistoryAsync(Guid id, PageRequest page, CancellationToken cancellationToken);
    Task<WorkOrderDto> CreateAsync(CreateWorkOrderRequest request, CancellationToken cancellationToken);
    Task<WorkOrderDto> UpdateAsync(Guid id, UpdateWorkOrderRequest request, CancellationToken cancellationToken);
    Task<WorkOrderDto> ScheduleAsync(Guid id, ScheduleWorkOrderRequest request, CancellationToken cancellationToken);
    Task<WorkOrderDto> AssignAsync(Guid id, AssignWorkOrderRequest request, CancellationToken cancellationToken);
    Task<WorkOrderDto> ExecuteAsync(Guid id, WorkOrderAction action, WorkOrderActionRequest request, CancellationToken cancellationToken);
}
