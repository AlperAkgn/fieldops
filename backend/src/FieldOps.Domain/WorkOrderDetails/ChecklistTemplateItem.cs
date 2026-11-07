using FieldOps.Domain.Organizations;

namespace FieldOps.Domain.WorkOrderDetails;

public sealed class ChecklistTemplateItem : ITenantEntity
{
    public const int MaximumLabelLength = 240;
    public const int MaximumPosition = 999;

    private ChecklistTemplateItem() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TemplateId { get; private set; }
    public string Label { get; private set; } = "";
    public int Position { get; private set; }
    public bool IsRequired { get; private set; }
    public bool IsActive { get; private set; }

    public static ChecklistTemplateItem Create(ChecklistTemplate template, string? label, int position, bool isRequired)
    {
        var item = new ChecklistTemplateItem
        {
            Id = Guid.NewGuid(),
            TenantId = template.TenantId,
            TemplateId = template.Id,
            IsActive = true
        };
        item.Update(label, position, isRequired);
        return item;
    }

    public void Update(string? label, int position, bool isRequired)
    {
        if (position is < 0 or > MaximumPosition)
            throw new DetailRuleException("invalid_item_position", $"Position must be between 0 and {MaximumPosition}.");
        Label = DetailText.Required(label, MaximumLabelLength, "invalid_item_label");
        Position = position;
        IsRequired = isRequired;
    }

    public void Archive() => IsActive = false;
}
