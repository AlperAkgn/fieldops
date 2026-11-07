using FieldOps.Domain.Organizations;

namespace FieldOps.Domain.WorkOrderDetails;

public sealed class WorkOrderChecklistItem : ITenantEntity
{
    private WorkOrderChecklistItem() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public Guid SourceTemplateId { get; private set; }
    public long SourceTemplateVersion { get; private set; }
    public Guid SourceTemplateItemId { get; private set; }
    public string Label { get; private set; } = "";
    public int Position { get; private set; }
    public bool IsRequired { get; private set; }
    public bool IsCompleted { get; private set; }
    public Guid? CompletedByUserId { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public static WorkOrderChecklistItem Snapshot(Guid workOrderId, ChecklistTemplate template, ChecklistTemplateItem item)
    {
        if (!template.IsActive || !item.IsActive || template.Id != item.TemplateId || template.TenantId != item.TenantId)
            throw new DetailRuleException("invalid_checklist_source", "Only active items from the selected checklist template can be copied.", true);

        return new WorkOrderChecklistItem
        {
            Id = Guid.NewGuid(),
            TenantId = template.TenantId,
            WorkOrderId = workOrderId,
            SourceTemplateId = template.Id,
            SourceTemplateVersion = template.Version,
            SourceTemplateItemId = item.Id,
            Label = item.Label,
            Position = item.Position,
            IsRequired = item.IsRequired
        };
    }

    public void SetCompletion(bool completed, Guid actorId, DateTimeOffset now)
    {
        if (IsCompleted == completed)
            return;

        IsCompleted = completed;
        CompletedByUserId = completed ? actorId : null;
        CompletedAt = completed ? now : null;
    }
}
