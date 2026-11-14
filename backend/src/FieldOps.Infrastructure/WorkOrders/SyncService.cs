using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using FieldOps.Application.Common;
using FieldOps.Application.Identity;
using FieldOps.Application.Operations;
using FieldOps.Application.WorkOrderDetails;
using FieldOps.Application.WorkOrders;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.WorkOrderDetails;
using FieldOps.Domain.WorkOrders;
using FieldOps.Infrastructure.Operations;
using FieldOps.Infrastructure.Persistence;
using FieldOps.Infrastructure.WorkOrderDetails;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.WorkOrders;

internal sealed class SyncService(FieldOpsDbContext database, OperationalWriteContext writes,
    WorkOrderService orders, WorkOrderDetailService details, ISessionValidator sessions, IDataProtectionProvider protection) : ISyncService
{
    private const int PageSize = 20;
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan CursorWindow = TimeSpan.FromDays(7);
    private readonly IDataProtector cursorProtector = protection.CreateProtector("FieldOps.Sync.Cursor.v1");

    public async Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken)
    {
        ValidateProtocol(request.Protocol);
        RequireTechnician();
        if (request.Operations is null || request.Operations.Count is < 1 or > 20 || request.Operations.Any(item => item is null))
            throw new FieldOpsException(ErrorKind.Validation, "sync_batch_size", "Submit one to twenty sync operations.");

        var results = new List<SyncOperationResult>();
        var blocked = new HashSet<Guid>();
        foreach (var operation in request.Operations)
        {
            if (blocked.Contains(operation.OrderId))
            {
                results.Add(new(operation.Id, "blocked", Code: "sync_predecessor_failed"));
                continue;
            }

            SyncOperationResult result;
            try { result = await ApplyAsync(operation, cancellationToken); }
            catch (FieldOpsException error) when (error.Kind is ErrorKind.Validation or ErrorKind.Conflict or ErrorKind.NotFound)
            {
                result = new(operation.Id, error.Kind == ErrorKind.Conflict ? "conflict" : "rejected", Code: error.Code);
            }
            finally
            {
                // Each operation has committed or disposed its own transaction.
                // Tracked entries from a failed write cannot escape into the next operation.
                database.ChangeTracker.Clear();
            }

            results.Add(result);
            if (result.Status != "applied") blocked.Add(operation.OrderId);
        }
        return new SyncPushResponse(results);
    }

    private async Task<SyncOperationResult> ApplyAsync(SyncOperation operation, CancellationToken cancellationToken)
    {
        ValidateOperation(operation);
        var actor = writes.Actor;
        await using var transaction = await writes.BeginAsync(cancellationToken);
        var visible = WorkOrderAccess.VisibleTo(database, actor).AsNoTracking().Where(row => row.Id == operation.OrderId);
        var currentVersion = await visible.Select(row => (long?)row.Version).SingleOrDefaultAsync(cancellationToken)
            ?? throw WorkOrderAccess.NotFound();

        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(operation with
        { CreatedAt = operation.CreatedAt.ToUniversalTime() })));
        var existing = await database.SyncReceipts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.TenantId == actor.TenantId && row.UserId == actor.UserId && row.OperationId == operation.Id, cancellationToken);
        if (existing is not null)
        {
            if (existing.Fingerprint != fingerprint)
                return new(operation.Id, "conflict", Code: "sync_operation_reused");
            await transaction.CommitAsync(cancellationToken);
            return new(operation.Id, "applied", existing.Version, Duplicate: true);
        }

        var now = writes.Now;
        if (operation.CreatedAt < now - ReplayWindow || operation.CreatedAt > now.AddMinutes(5))
            return new(operation.Id, "rejected", Code: "sync_operation_expired");
        if (currentVersion != operation.ExpectedVersion)
            return new(operation.Id, "conflict", currentVersion, "work_order_version_conflict");

        var resultingVersion = operation.Kind switch
        {
            "checklist" => (await details.UpdateInTransactionAsync(operation.OrderId, operation.ItemId!.Value,
                new(operation.IsCompleted, operation.ExpectedVersion), cancellationToken)).WorkOrderVersion,
            "note" => (await details.AddCommentInTransactionAsync(operation.OrderId,
                new(operation.Body!, CommentVisibility.TechnicianVisible, operation.ExpectedVersion), cancellationToken)).WorkOrderVersion,
            _ => (await orders.ExecuteInTransactionAsync(operation.OrderId, ParseAction(operation.Kind),
                new(operation.ExpectedVersion), cancellationToken)).Version
        };
        database.SyncReceipts.Add(new(actor.TenantId, actor.UserId, operation.Id, operation.OrderId,
            fingerprint, resultingVersion, operation.CreatedAt.ToUniversalTime(), now));
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(operation.Id, "applied", resultingVersion);
    }

    public async Task<SyncPullResponse> PullAsync(int protocol, string? cursor, CancellationToken cancellationToken)
    {
        ValidateProtocol(protocol);
        RequireTechnician();
        var actor = writes.Actor;
        var now = writes.Now;
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var current = await sessions.ValidateSessionAsync(actor.UserId, actor.SessionId, actor.TenantId, cancellationToken);
        if (current is null)
            throw new FieldOpsException(ErrorKind.Unauthenticated, "session_invalid", "The current session is unavailable.");
        if (current.Role != OrganizationRole.Technician)
            throw new FieldOpsException(ErrorKind.Forbidden, "membership_changed", "Organization access changed.");

        // Assignment identity changes invalidate even a remove/reassign cycle between pulls.
        // Project only IDs. No prior tenant/order payload is carried in the cursor.
        var assigned = await database.WorkOrderAssignments.AsNoTracking()
            .Where(row => row.TenantId == actor.TenantId && row.TechnicianUserId == actor.UserId && row.RemovedAt == null)
            .OrderBy(row => row.Id).Select(row => row.Id).ToListAsync(cancellationToken);
        var authorization = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(assigned)));
        var watermark = await database.SyncWatermarks.AsNoTracking().Where(row => row.TenantId == actor.TenantId)
            .Select(row => (long?)row.Position).SingleOrDefaultAsync(cancellationToken) ?? 0;
        var previous = ReadCursor(cursor, now);
        if (previous is not null && (previous.TenantId != actor.TenantId || previous.UserId != actor.UserId ||
            previous.SessionId != actor.SessionId || previous.Authorization != authorization || previous.Position > watermark))
            throw Resnapshot();

        var after = previous?.Position ?? 0;
        var visible = WorkOrderAccess.VisibleTo(database, actor).AsNoTracking();
        var page = await (from change in database.SyncWorkOrderChanges.AsNoTracking()
                          join order in visible on new { change.TenantId, Id = change.WorkOrderId } equals new { order.TenantId, order.Id }
                          where change.TenantId == actor.TenantId && change.Position > after && change.Position <= watermark
                          orderby change.Position
                          select new { Order = order, change.Position }).Take(PageSize + 1).ToListAsync(cancellationToken);
        var more = page.Count > PageSize;
        var result = new List<SyncOrder>();
        foreach (var row in page.Take(PageSize))
        {
            var checklist = await details.GetChecklistAsync(row.Order.Id, cancellationToken);
            var notes = await details.ListCommentsAsync(row.Order.Id, new PageRequest(1, 10), cancellationToken);
            result.Add(new(WorkOrderService.ToDto(row.Order), checklist, notes.Items));
        }
        var position = more ? page[PageSize - 1].Position : watermark;
        var next = cursorProtector.Protect(JsonSerializer.Serialize(
            new CursorState(actor.TenantId, actor.UserId, actor.SessionId, authorization, position, now)));
        await transaction.CommitAsync(cancellationToken);
        return new(next, more, now, result);
    }

    private CursorState? ReadCursor(string? cursor, DateTimeOffset now)
    {
        if (cursor is null) return null;
        if (cursor.Length > 4096) throw Resnapshot();
        try
        {
            var state = JsonSerializer.Deserialize<CursorState>(cursorProtector.Unprotect(cursor));
            if (state is null || state.Position < 0 || state.IssuedAt < now - CursorWindow || state.IssuedAt > now)
                throw Resnapshot();
            return state;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        {
            throw Resnapshot();
        }
    }

    private void RequireTechnician()
    {
        if (writes.Actor.Role != OrganizationRole.Technician)
            throw new FieldOpsException(ErrorKind.Forbidden, "sync_technician_required", "Synchronization requires a technician membership.");
    }

    private static void ValidateProtocol(int protocol)
    {
        if (protocol != 1)
            throw new FieldOpsException(ErrorKind.Validation, "sync_protocol_unsupported", "Update the mobile application to synchronize.");
    }

    private static void ValidateOperation(SyncOperation operation)
    {
        var validPayload = operation.Kind switch
        {
            "checklist" => operation.ItemId is Guid id && id != Guid.Empty && operation.IsCompleted is not null && operation.Body is null,
            "note" => operation.ItemId is null && operation.IsCompleted is null && operation.Body is { Length: > 0 and <= 4000 },
            "start" or "pause" or "resume" or "complete" => operation.ItemId is null && operation.IsCompleted is null && operation.Body is null,
            _ => false
        };
        if (operation.Id == Guid.Empty || operation.OrderId == Guid.Empty || operation.ExpectedVersion < 1 || !validPayload)
            throw new FieldOpsException(ErrorKind.Validation, "sync_invalid_operation", "The offline operation is invalid.");
    }

    private static WorkOrderAction ParseAction(string kind) => kind switch
    {
        "start" => WorkOrderAction.Start,
        "pause" => WorkOrderAction.Pause,
        "resume" => WorkOrderAction.Resume,
        "complete" => WorkOrderAction.Complete,
        _ => throw new InvalidOperationException("Unsupported sync action.")
    };

    private static FieldOpsException Resnapshot() => new(ErrorKind.Conflict, "sync_resnapshot_required",
        "Cached access changed. Download a new authorized snapshot.");

    private sealed record CursorState(Guid TenantId, Guid UserId, Guid SessionId, string Authorization,
        long Position, DateTimeOffset IssuedAt);
}
