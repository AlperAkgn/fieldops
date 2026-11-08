using FieldOps.Application.Operations;

namespace FieldOps.Application.Reporting;

public sealed record ReportFilter(DateTimeOffset? From = null, DateTimeOffset? To = null);
public sealed record TechnicianFilter(int? Page = null, int? PageSize = null, string? Search = null);
public sealed record TechnicianDto(Guid UserId, string DisplayName);
public sealed record DistributionDto(string Name, int Count);
public sealed record WorkloadDto(Guid UserId, string DisplayName, int ActiveOrders);
public sealed record UpcomingOrderDto(Guid Id, string WorkOrderNumber, string Title, string Status, DateTimeOffset? PlannedStartAt);
public sealed record ActivityDto(Guid Id, string ResourceType, Guid ResourceId, string Action, DateTimeOffset OccurredAt);
public sealed record OperationsReportDto(int TotalOrders, int ActiveOrders, int CompletedOrders, int OverdueOrders,
    int ActiveTechnicians, decimal CompletionRate, IReadOnlyList<DistributionDto> StatusDistribution,
    IReadOnlyList<DistributionDto> PriorityDistribution, IReadOnlyList<WorkloadDto> TechnicianWorkload,
    IReadOnlyList<UpcomingOrderDto> UpcomingOrders, IReadOnlyList<ActivityDto> RecentActivities);

public interface IReportingReader
{
    Task<OperationsReportDto> ReportAsync(ReportFilter filter, CancellationToken cancellationToken);
    Task<PageResult<TechnicianDto>> TechniciansAsync(TechnicianFilter filter, CancellationToken cancellationToken);
}
