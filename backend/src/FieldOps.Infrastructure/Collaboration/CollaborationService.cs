using System.Security.Cryptography;
using System.Text;
using FieldOps.Application.Collaboration;
using FieldOps.Application.Common;
using FieldOps.Application.Identity;
using FieldOps.Application.Operations;
using FieldOps.Application.Tenancy;
using FieldOps.Domain.Organizations;
using FieldOps.Infrastructure.Operations;
using FieldOps.Infrastructure.Persistence;
using FieldOps.Infrastructure.WorkOrders;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Collaboration;

internal sealed class CollaborationService(FieldOpsDbContext db, OperationalWriteContext writes,
    ISessionValidator sessions, IDataProtectionProvider protection) : ICollaborationAccess, INotifications
{
    public Task<TenantActor?> ValidateAsync(TenantActor actor, CancellationToken cancellationToken) =>
        sessions.ValidateSessionAsync(actor.UserId, actor.SessionId, actor.TenantId, cancellationToken);

    public async Task<bool> CanReadOrderAsync(TenantActor actor, Guid orderId, CancellationToken cancellationToken)
    {
        using var system = db.BeginSystemScope();
        return await WorkOrderAccess.VisibleTo(db, actor).AnyAsync(row => row.Id == orderId, cancellationToken);
    }

    public async Task<PageResult<NotificationDto>> ListAsync(PageRequest page, CancellationToken cancellationToken)
    {
        page.Validate();
        var actor = writes.Actor;
        var own = db.InboxNotifications.AsNoTracking().Where(row => row.TenantId == actor.TenantId && row.UserId == actor.UserId);
        var visible = WorkOrderAccess.VisibleTo(db, actor);
        var items = await own.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
            .Skip(page.Offset).Take(page.PageSize)
            .Select(row => new NotificationDto(row.Id,
                visible.Any(order => order.Id == row.WorkOrderId) ? row.WorkOrderId : null,
                row.Kind, row.CreatedAt, row.ReadAt)).ToListAsync(cancellationToken);
        return new(items, page.Page, page.PageSize, await own.CountAsync(cancellationToken));
    }

    public async Task MarkReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = writes.Actor;
        var now = writes.Now;
        await using var transaction = await writes.BeginAsync(cancellationToken);
        var count = await db.InboxNotifications.Where(row => row.TenantId == actor.TenantId && row.UserId == actor.UserId && row.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ReadAt, row => row.ReadAt ?? now), cancellationToken);
        if (count == 0) throw new FieldOpsException(ErrorKind.NotFound, "notification_not_found", "The notification was not found.");
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RegisterAsync(Guid deviceId, RegisterPushDevice request, CancellationToken cancellationToken)
    {
        var actor = writes.Actor;
        if (actor.Role != OrganizationRole.Technician)
            throw new FieldOpsException(ErrorKind.Forbidden, "technician_required", "Mobile push requires a technician membership.");
        if (deviceId == Guid.Empty || !IsExpoToken(request.Token))
            throw new FieldOpsException(ErrorKind.Validation, "invalid_push_device", "Provide a valid Expo push token and device ID.");
        await using var transaction = await writes.BeginAsync(cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));
        // Serialize ownership changes across workspaces for the same installation/token.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({hash}, 0))", cancellationToken);
        using var system = db.BeginSystemScope();
        var now = writes.Now;
        if (await db.PushDevices.AnyAsync(row => row.TokenHash == hash && row.RevokedAt == null && row.DeviceId != deviceId, cancellationToken))
            throw new FieldOpsException(ErrorKind.Conflict, "push_token_already_registered", "This notification token is already registered.");
        await db.PushDevices.Where(row => row.TokenHash == hash && row.RevokedAt == null &&
                (row.TenantId != actor.TenantId || row.DeviceId != deviceId || row.SessionId != actor.SessionId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAt, now)
                .SetProperty(row => row.ProtectedToken, ""), cancellationToken);
        var device = await db.PushDevices.FindAsync([actor.TenantId, deviceId, actor.SessionId], cancellationToken);
        if (device is null)
        {
            device = new PushDevice(actor.TenantId, deviceId, actor.SessionId, actor.UserId);
            db.PushDevices.Add(device);
        }
        device.TokenHash = hash;
        device.ProtectedToken = protection.CreateProtector("FieldOps.PushDevice.v1").Protect(request.Token);
        device.LastSeenAt = now;
        device.RevokedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RevokeAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var actor = writes.Actor;
        var now = writes.Now;
        await using var transaction = await writes.BeginAsync(cancellationToken);
        await db.PushDevices.Where(row => row.TenantId == actor.TenantId && row.UserId == actor.UserId &&
                row.SessionId == actor.SessionId && row.DeviceId == deviceId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAt, now)
                .SetProperty(row => row.ProtectedToken, ""), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static bool IsExpoToken(string? token)
    {
        if (token is null || token.Length is < 17 or > 160 || token[^1] != ']') return false;
        var start = token.StartsWith("ExpoPushToken[", StringComparison.Ordinal) ? 14 :
            token.StartsWith("ExponentPushToken[", StringComparison.Ordinal) ? 18 : 0;
        return start > 0 && token.Length > start + 1 && token[start..^1].All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }
}
