using FieldOps.Domain.Organizations;

namespace FieldOps.Domain.WorkOrderDetails;

public enum CommentVisibility { TechnicianVisible, Internal }

public sealed class WorkOrderComment : ITenantEntity
{
    public const int MaximumBodyLength = 4000;

    private WorkOrderComment() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Body { get; private set; } = "";
    public CommentVisibility Visibility { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static WorkOrderComment Create(Guid tenantId, Guid workOrderId, Guid authorUserId, string? body,
        CommentVisibility visibility, DateTimeOffset now)
    {
        if (!Enum.IsDefined(visibility))
            throw new DetailRuleException("invalid_comment_visibility", "Provide a valid comment visibility.");

        return new WorkOrderComment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkOrderId = workOrderId,
            AuthorUserId = authorUserId,
            Body = DetailText.Required(body, MaximumBodyLength, "invalid_comment_body", multiline: true),
            Visibility = visibility,
            CreatedAt = now
        };
    }
}
