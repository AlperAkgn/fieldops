using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests;

public sealed class WorkOrderDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> TransitionPairs()
    {
        var valid = new HashSet<(WorkOrderStatus, WorkOrderStatus)>
        {
            (WorkOrderStatus.Draft, WorkOrderStatus.Scheduled), (WorkOrderStatus.Draft, WorkOrderStatus.Cancelled),
            (WorkOrderStatus.Scheduled, WorkOrderStatus.Assigned), (WorkOrderStatus.Scheduled, WorkOrderStatus.Cancelled),
            (WorkOrderStatus.Assigned, WorkOrderStatus.InProgress), (WorkOrderStatus.Assigned, WorkOrderStatus.Cancelled),
            (WorkOrderStatus.InProgress, WorkOrderStatus.OnHold), (WorkOrderStatus.InProgress, WorkOrderStatus.Completed),
            (WorkOrderStatus.OnHold, WorkOrderStatus.InProgress), (WorkOrderStatus.OnHold, WorkOrderStatus.Cancelled)
        };
        foreach (var from in Enum.GetValues<WorkOrderStatus>())
            foreach (var to in Enum.GetValues<WorkOrderStatus>())
                yield return [from, to, valid.Contains((from, to))];
    }

    [Theory]
    [MemberData(nameof(TransitionPairs))]
    public void OnlyTheSpecifiedTransitionsAreAllowed(WorkOrderStatus from, WorkOrderStatus to, bool allowed) =>
        Assert.Equal(allowed, WorkOrder.CanTransition(from, to));

    [Fact]
    public void RequiredChecklistGatesCompletionAndTerminalStatePreservesHistory()
    {
        var order = NewOrder();
        order.Schedule(1, Now, Now.AddHours(1), Now);
        order.Assign(2, Now);
        order.Execute(WorkOrderAction.Start, 3, Now);
        var failed = Assert.Throws<WorkOrderRuleException>(() =>
            order.Execute(WorkOrderAction.Complete, 4, Now.AddMinutes(10), hasIncompleteRequiredItems: true));
        Assert.Equal("required_checklist_incomplete", failed.Code);
        Assert.Equal(4, order.Version);
        Assert.Equal(WorkOrderStatus.InProgress, order.Status);
        Assert.Null(order.ActualEndAt);
        order.Execute(WorkOrderAction.Pause, 4, Now.AddMinutes(10));
        order.Execute(WorkOrderAction.Resume, 5, Now.AddMinutes(15));
        Assert.Equal(Now, order.ActualStartAt);
        order.Execute(WorkOrderAction.Complete, 6, Now.AddMinutes(30));
        Assert.Equal(Now.AddMinutes(30), order.ActualEndAt);
        Assert.Throws<WorkOrderRuleException>(() => order.Execute(WorkOrderAction.Start, 7, Now));
        Assert.Throws<WorkOrderRuleException>(() => order.Touch(7, Now));
    }

    [Fact]
    public void InvalidVersionsIntervalsAndReferencesLeaveTheAggregateUnchanged()
    {
        var order = NewOrder();
        Assert.Throws<WorkOrderRuleException>(() => order.Schedule(0, Now, Now.AddHours(1), Now));
        Assert.Throws<WorkOrderRuleException>(() => order.Schedule(2, Now, Now.AddHours(1), Now));
        Assert.Throws<WorkOrderRuleException>(() => order.Schedule(1, Now, Now, Now));
        Assert.Throws<WorkOrderRuleException>(() => order.Assign(1, Now));
        Assert.Equal(1, order.Version);
        Assert.Equal(WorkOrderStatus.Draft, order.Status);
        order.Schedule(1, Now.ToOffset(TimeSpan.FromHours(3)), Now.AddHours(1), Now);
        Assert.Equal(TimeSpan.Zero, order.PlannedStartAt!.Value.Offset);
        Assert.Throws<WorkOrderRuleException>(() => order.Update(2, Guid.NewGuid(), order.ServiceLocationId,
            null, "Changed", null, WorkOrderPriority.High, Now));
        Assert.Equal("Inspection", order.Title);
    }

    private static WorkOrder NewOrder() => WorkOrder.Create(Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), null, "Inspection", null, WorkOrderPriority.Medium, Now);
}
