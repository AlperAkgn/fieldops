using FieldOps.Application.Common;
using FieldOps.Application.Tenancy;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.WorkOrders;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.WorkOrders;

internal static class WorkOrderAccess
{
    public static IQueryable<WorkOrder> VisibleTo(FieldOpsDbContext database, TenantActor actor)
    {
        var orders = database.WorkOrders.Where(order => order.TenantId == actor.TenantId);
        return actor.Role == OrganizationRole.Technician
            ? orders.Where(order => database.WorkOrderAssignments.Any(assignment => assignment.TenantId == actor.TenantId
                && assignment.WorkOrderId == order.Id && assignment.TechnicianUserId == actor.UserId && assignment.RemovedAt == null))
            : orders;
    }

    public static async Task<WorkOrder> LoadAsync(FieldOpsDbContext database, TenantActor actor, Guid id,
        CancellationToken cancellationToken)
        => await VisibleTo(database, actor).SingleOrDefaultAsync(order => order.Id == id, cancellationToken) ?? throw NotFound();

    public static void RequireManager(TenantActor actor)
    {
        if (actor.Role is not (OrganizationRole.OrganizationOwner or OrganizationRole.Manager)) throw Forbidden();
    }

    public static void RequireExecution(TenantActor actor)
    {
        if (actor.Role != OrganizationRole.Technician) RequireManager(actor);
    }

    public static void ApplyRule(Action apply)
    {
        try { apply(); }
        catch (WorkOrderRuleException exception) { throw RuleError(exception); }
    }

    public static FieldOpsException RuleError(WorkOrderRuleException exception) =>
        new(exception.IsConflict ? ErrorKind.Conflict : ErrorKind.Validation, exception.Code, exception.Message);

    public static FieldOpsException NotFound() =>
        new(ErrorKind.NotFound, "work_order_not_found", "The work order was not found.");

    private static FieldOpsException Forbidden() =>
        new(ErrorKind.Forbidden, "work_order_role_required", "Your organization role cannot perform this work order action.");
}
