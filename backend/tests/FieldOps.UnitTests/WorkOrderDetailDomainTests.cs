using FieldOps.Domain.WorkOrderDetails;

namespace FieldOps.UnitTests;

public sealed class WorkOrderDetailDomainTests
{
    [Fact]
    public void SnapshotCopiesDefinitionAndVersionIndependentlyOfSubsequentTemplateEdits()
    {
        var now = DateTimeOffset.UtcNow;
        var template = ChecklistTemplate.Create(Guid.NewGuid(), "Inspection", now);
        var source = ChecklistTemplateItem.Create(template, "Check pressure", 4, true);
        template.Touch(1, now);
        var snapshot = WorkOrderChecklistItem.Snapshot(Guid.NewGuid(), template, source);

        template.Rename("Updated inspection", 2, now);
        source.Update("Updated label", 0, false);
        source.Archive();

        Assert.Equal(2, snapshot.SourceTemplateVersion);
        Assert.Equal(template.Id, snapshot.SourceTemplateId);
        Assert.Equal(source.Id, snapshot.SourceTemplateItemId);
        Assert.Equal("Check pressure", snapshot.Label);
        Assert.Equal(4, snapshot.Position);
        Assert.True(snapshot.IsRequired);
        Assert.False(snapshot.IsCompleted);
    }

    [Fact]
    public void SnapshotCannotCopyAnItemFromAnotherTemplateOrArchivedSource()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var first = ChecklistTemplate.Create(tenantId, "First", now);
        var second = ChecklistTemplate.Create(tenantId, "Second", now);
        var source = ChecklistTemplateItem.Create(second, "Check pressure", 0, true);

        Assert.Throws<DetailRuleException>(() => WorkOrderChecklistItem.Snapshot(Guid.NewGuid(), first, source));
        second.Archive(1, now);
        Assert.Throws<DetailRuleException>(() => WorkOrderChecklistItem.Snapshot(Guid.NewGuid(), second, source));
    }

    [Fact]
    public void CompletingAndReopeningTracksActorAndTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var template = ChecklistTemplate.Create(Guid.NewGuid(), "Inspection", now);
        var source = ChecklistTemplateItem.Create(template, "Check pressure", 0, true);
        var snapshot = WorkOrderChecklistItem.Snapshot(Guid.NewGuid(), template, source);
        var actor = Guid.NewGuid();

        snapshot.SetCompletion(true, actor, now);
        snapshot.SetCompletion(true, Guid.NewGuid(), now.AddMinutes(1));
        Assert.True(snapshot.IsCompleted);
        Assert.Equal(actor, snapshot.CompletedByUserId);
        Assert.Equal(now, snapshot.CompletedAt);

        snapshot.SetCompletion(false, actor, now.AddMinutes(2));
        Assert.False(snapshot.IsCompleted);
        Assert.Null(snapshot.CompletedByUserId);
        Assert.Null(snapshot.CompletedAt);
    }

    [Fact]
    public void StaleTemplateMutationAndArchiveAreRejectedWithoutChangingDefinition()
    {
        var now = DateTimeOffset.UtcNow;
        var template = ChecklistTemplate.Create(Guid.NewGuid(), "Inspection", now);
        template.Rename("Revised", 1, now.AddMinutes(1));
        var stale = Assert.Throws<DetailRuleException>(() => template.Rename("Old edit", 1, now.AddMinutes(2)));
        Assert.Equal("version_conflict", stale.Code);
        Assert.True(stale.IsConflict);
        Assert.Equal("Revised", template.Name);

        template.Archive(2, now.AddMinutes(2));
        var archived = Assert.Throws<DetailRuleException>(() => template.Touch(3, now.AddMinutes(3)));
        Assert.Equal("checklist_template_archived", archived.Code);
        Assert.False(template.IsActive);
        Assert.Equal(3, template.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A\0B")]
    [InlineData("A\u0085B")]
    public void InvalidCommentBodiesAreRejected(string body)
    {
        var invalid = Assert.Throws<DetailRuleException>(() => WorkOrderComment.Create(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), body, CommentVisibility.TechnicianVisible, DateTimeOffset.UtcNow));
        Assert.Equal("invalid_comment_body", invalid.Code);
    }
}
