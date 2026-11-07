using FieldOps.Domain.Organizations;

namespace FieldOps.Domain.WorkOrders;

public enum WorkOrderPriority { Low, Medium, High, Critical }
public enum WorkOrderStatus { Draft, Scheduled, Assigned, InProgress, OnHold, Completed, Cancelled }
public enum WorkOrderAction { Start, Pause, Resume, Complete, Cancel }

public sealed class WorkOrderRuleException(string code, string message, bool isConflict) : Exception(message)
{
    public string Code { get; } = code;
    public bool IsConflict { get; } = isConflict;
}

public sealed class WorkOrder : ITenantEntity
{
    private WorkOrder() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string WorkOrderNumber { get; private set; } = "";
    public Guid CustomerId { get; private set; }
    public Guid ServiceLocationId { get; private set; }
    public Guid? AssetId { get; private set; }
    public string Title { get; private set; } = "";
    public string? Description { get; private set; }
    public WorkOrderPriority Priority { get; private set; }
    public WorkOrderStatus Status { get; private set; }
    public DateTimeOffset? PlannedStartAt { get; private set; }
    public DateTimeOffset? PlannedEndAt { get; private set; }
    public DateTimeOffset? ActualStartAt { get; private set; }
    public DateTimeOffset? ActualEndAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    public static WorkOrder Create(Guid tenantId, Guid creatorId, Guid customerId, Guid locationId, Guid? assetId,
        string title, string? description, WorkOrderPriority priority, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || creatorId == Guid.Empty) throw new ArgumentException("An actor and tenant are required.");
        var id = Guid.NewGuid();
        var order = new WorkOrder
        {
            Id = id,
            TenantId = tenantId,
            WorkOrderNumber = $"WO-{id:N}",
            Status = WorkOrderStatus.Draft,
            CreatedByUserId = creatorId,
            CreatedAt = now.ToUniversalTime(),
            UpdatedAt = now.ToUniversalTime(),
            Version = 1
        };
        order.SetDetails(customerId, locationId, assetId, title, description, priority);
        return order;
    }

    public void Update(long expectedVersion, Guid customerId, Guid locationId, Guid? assetId, string title,
        string? description, WorkOrderPriority priority, DateTimeOffset now)
    {
        VerifyEditable(expectedVersion);
        if (Status != WorkOrderStatus.Draft && (CustomerId != customerId || ServiceLocationId != locationId || AssetId != assetId))
            throw Conflict("work_order_references_locked", "Customer, location and asset can only change while the work order is a draft.");
        SetDetails(customerId, locationId, assetId, title, description, priority);
        Advance(now);
    }

    public void Schedule(long expectedVersion, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        VerifyVersion(expectedVersion);
        if (Status is not (WorkOrderStatus.Draft or WorkOrderStatus.Scheduled or WorkOrderStatus.Assigned))
            throw Conflict("invalid_work_order_transition", "This work order cannot be scheduled in its current state.");
        if (end <= start)
            throw Invalid("invalid_schedule", "The planned end must be after the planned start.");

        PlannedStartAt = start.ToUniversalTime();
        PlannedEndAt = end.ToUniversalTime();
        if (Status == WorkOrderStatus.Draft) Status = WorkOrderStatus.Scheduled;
        Advance(now);
    }

    public void Assign(long expectedVersion, DateTimeOffset now)
    {
        VerifyVersion(expectedVersion);
        if (Status is not (WorkOrderStatus.Scheduled or WorkOrderStatus.Assigned) || PlannedStartAt is null || PlannedEndAt is null)
            throw Conflict("invalid_work_order_transition", "Only scheduled or assigned work orders can receive assignments.");
        Status = WorkOrderStatus.Assigned;
        Advance(now);
    }

    public void Execute(WorkOrderAction action, long expectedVersion, DateTimeOffset now, bool hasIncompleteRequiredItems = false)
    {
        VerifyVersion(expectedVersion);
        var next = action switch
        {
            WorkOrderAction.Start when Status == WorkOrderStatus.Assigned => WorkOrderStatus.InProgress,
            WorkOrderAction.Resume when Status == WorkOrderStatus.OnHold => WorkOrderStatus.InProgress,
            WorkOrderAction.Pause when Status == WorkOrderStatus.InProgress => WorkOrderStatus.OnHold,
            WorkOrderAction.Complete when Status == WorkOrderStatus.InProgress => WorkOrderStatus.Completed,
            WorkOrderAction.Cancel when CanTransition(Status, WorkOrderStatus.Cancelled) => WorkOrderStatus.Cancelled,
            _ => throw Conflict("invalid_work_order_transition", "The requested action is unavailable in the current state.")
        };
        if (next == WorkOrderStatus.Completed && hasIncompleteRequiredItems)
            throw Conflict("required_checklist_incomplete", "Complete all required checklist items before completing the work order.");
        if (next is WorkOrderStatus.Completed or WorkOrderStatus.Cancelled && ActualStartAt is { } started)
        {
            if (now < started) throw Conflict("invalid_actual_time", "The actual end cannot precede the actual start.");
            ActualEndAt = now.ToUniversalTime();
        }

        if (action == WorkOrderAction.Start) ActualStartAt = now.ToUniversalTime();
        Status = next;
        Advance(now);
    }

    public void Touch(long expectedVersion, DateTimeOffset now)
    {
        VerifyEditable(expectedVersion);
        Advance(now);
    }

    public void VerifyVersion(long expectedVersion)
    {
        if (expectedVersion < 1) throw Invalid("invalid_version", "A positive expected version is required.");
        if (Version != expectedVersion)
            throw Conflict("work_order_version_conflict", "The work order changed. Reload it before retrying.");
    }

    public static bool CanTransition(WorkOrderStatus from, WorkOrderStatus to) => (from, to) switch
    {
        (WorkOrderStatus.Draft, WorkOrderStatus.Scheduled or WorkOrderStatus.Cancelled) => true,
        (WorkOrderStatus.Scheduled, WorkOrderStatus.Assigned or WorkOrderStatus.Cancelled) => true,
        (WorkOrderStatus.Assigned, WorkOrderStatus.InProgress or WorkOrderStatus.Cancelled) => true,
        (WorkOrderStatus.InProgress, WorkOrderStatus.OnHold or WorkOrderStatus.Completed) => true,
        (WorkOrderStatus.OnHold, WorkOrderStatus.InProgress or WorkOrderStatus.Cancelled) => true,
        _ => false
    };

    private void VerifyEditable(long expectedVersion)
    {
        VerifyVersion(expectedVersion);
        if (Status is WorkOrderStatus.Completed or WorkOrderStatus.Cancelled)
            throw Conflict("work_order_terminal", "Terminal work orders cannot be changed.");
    }

    private void SetDetails(Guid customerId, Guid locationId, Guid? assetId, string title, string? description, WorkOrderPriority priority)
    {
        if (customerId == Guid.Empty || locationId == Guid.Empty || assetId == Guid.Empty)
            throw Invalid("invalid_work_order_reference", "Provide a customer, service location and a valid asset ID when applicable.");
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 160 || title.Any(char.IsControl))
            throw Invalid("invalid_work_order_title", "Provide a title of 1 to 160 characters without control characters.");
        if (description is not null && (description.Length > 4000 ||
            description.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t'))))
            throw Invalid("invalid_work_order_description", "Provide a description of up to 4000 characters.");
        if (!Enum.IsDefined(priority))
            throw Invalid("invalid_work_order_priority", "Provide a valid work order priority.");

        CustomerId = customerId;
        ServiceLocationId = locationId;
        AssetId = assetId;
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Priority = priority;
    }

    private void Advance(DateTimeOffset now)
    {
        Version = checked(Version + 1);
        if (now > UpdatedAt) UpdatedAt = now.ToUniversalTime();
    }

    private static WorkOrderRuleException Invalid(string code, string message) => new(code, message, false);
    private static WorkOrderRuleException Conflict(string code, string message) => new(code, message, true);
}
