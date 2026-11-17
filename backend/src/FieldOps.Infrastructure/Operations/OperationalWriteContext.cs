using FieldOps.Application.Common;
using FieldOps.Application.Identity;
using FieldOps.Application.Tenancy;
using FieldOps.Domain.Operations;
using FieldOps.Domain.WorkOrders;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FieldOps.Infrastructure.Operations;

internal sealed class OperationalWriteContext(FieldOpsDbContext database, TenantContext tenant,
    ISessionValidator sessions, TimeProvider clock)
{
    public void AttachmentAdded(Guid orderId, long version)
    {
        var audit = AuditLog.Record(Actor.TenantId, Actor.UserId, "work_order", orderId, "attachment_added", null, Now);
        database.AuditLogs.Add(audit);
        database.WorkOrderEvents.Add(new(audit.Id, Actor.TenantId, orderId, version, "attachment_added", audit.OccurredAt, [])
        { TraceParent = FieldOpsTelemetry.CurrentTraceParent });
    }

    public TenantActor Actor => tenant.Actor ??
        throw new FieldOpsException(ErrorKind.Unauthenticated, "authentication_required", "Authentication is required.");

    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task<IDbContextTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        var actor = Actor;
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Phase 1 membership writes take the same organization lock. Operations
            // lock this row before loading mutable aggregates or checking availability.
            var organization = await database.Organizations
                .FromSqlInterpolated($"""SELECT * FROM "Organizations" WHERE "Id" = {actor.TenantId} FOR UPDATE""")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (organization is null || !organization.IsActive)
                throw new FieldOpsException(ErrorKind.Unauthenticated, "session_invalid", "The current session is unavailable.");

            var current = await sessions.ValidateSessionAsync(actor.UserId, actor.SessionId, actor.TenantId, cancellationToken);
            if (current is null)
                throw new FieldOpsException(ErrorKind.Unauthenticated, "session_invalid", "The current session is unavailable.");
            if (current.Role != actor.Role)
                throw new FieldOpsException(ErrorKind.Forbidden, "membership_changed", "Organization access changed. Retry the request.");
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public void Audit(string resourceType, Guid resourceId, string action, string? reason = null,
        WorkOrderAuditMetadata? metadata = null, DateTimeOffset? occurredAt = null)
    {
        var audit = AuditLog.Record(Actor.TenantId, Actor.UserId, resourceType, resourceId, action, reason,
            occurredAt ?? Now, metadata);
        database.AuditLogs.Add(audit);
        if (resourceType != "work_order" || action == "availability_overridden") return;
        var order = database.ChangeTracker.Entries<WorkOrder>().Single(entry => entry.Entity.Id == resourceId).Entity;
        var assignmentChange = metadata?.Changes.TechnicianUserIds;
        var removed = assignmentChange?.Previous.Except(assignmentChange.Current).ToArray() ?? [];
        database.WorkOrderEvents.Add(new(audit.Id, Actor.TenantId, resourceId, order.Version, action, audit.OccurredAt, removed)
        { TraceParent = FieldOpsTelemetry.CurrentTraceParent });
        if (assignmentChange is not null)
        {
            foreach (var (user, kind) in assignmentChange.Current.Select(user =>
                (user, assignmentChange.Previous.Contains(user) ? "assignment_updated" : "assigned"))
                .Concat(removed.Select(user => (user, "unassigned"))))
                database.InboxNotifications.Add(new(Guid.NewGuid(), Actor.TenantId, user, resourceId, order.Version, kind, audit.OccurredAt));
        }
        else if (metadata?.Changes.PrimaryTechnicianUserId?.Current is { } primary)
            database.InboxNotifications.Add(new(Guid.NewGuid(), Actor.TenantId, primary, resourceId, order.Version, "assignment_updated", audit.OccurredAt));
    }
}
