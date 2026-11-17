using System.Diagnostics;
using FieldOps.Application.Collaboration;
using FieldOps.Application.Common;
using FieldOps.Domain.Organizations;
using FieldOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FieldOps.Infrastructure.Collaboration;

internal sealed class CollaborationDispatcher(FieldOpsDbContext db, IOrderChangeDelivery realtime, ICollaborationAccess access, IAttachmentStorage storage,
    IPushProvider push, IDataProtectionProvider protection, TimeProvider clock, ILogger<CollaborationDispatcher> logger)
    : ICollaborationDispatcher
{
    public async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await DispatchChangesAsync(cancellationToken);
        await MaintainAsync(cancellationToken);
    }

    public async Task<bool> DispatchChangesAsync(CancellationToken cancellationToken)
    {
        using var system = db.BeginSystemScope();
        for (var index = 0; index < 25; index++)
            if (!await DispatchChangeAsync(cancellationToken)) return false;
        return true;
    }

    public async Task MaintainAsync(CancellationToken cancellationToken)
    {
        using var system = db.BeginSystemScope();
        var now = clock.GetUtcNow();
        await InsertUpcomingAsync(now, cancellationToken);
        await CleanAbandonedAttachmentAsync(now, cancellationToken);
        if (!push.Enabled) return;
        await RetireInactiveDevicesAsync(now, cancellationToken);
        await QueuePushAsync(now, cancellationToken);
        for (var index = 0; index < 10 && await DispatchPushAsync(now, cancellationToken); index++) { }
    }

    public async Task ObserveBacklogAsync(CancellationToken cancellationToken)
    {
        using var system = db.BeginSystemScope();
        var events = await db.WorkOrderEvents.LongCountAsync(row => row.DispatchedAt == null && row.FailedAt == null, cancellationToken);
        var pushes = await db.PushDeliveries.LongCountAsync(row => row.CompletedAt == null, cancellationToken);
        FieldOpsTelemetry.ObservePending(events, pushes);
    }

    private Task<int> RetireInactiveDevicesAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "PushDevices" d SET "RevokedAt" = {now}, "ProtectedToken" = ''
            WHERE d."RevokedAt" IS NULL AND (d."LastSeenAt" <= {now.AddDays(-30)} OR NOT EXISTS (
                SELECT 1 FROM "AuthSessions" s
                JOIN "OrganizationMemberships" m ON m."TenantId" = s."TenantId" AND m."UserId" = s."UserId"
                JOIN "AspNetUsers" u ON u."Id" = s."UserId"
                JOIN "Organizations" o ON o."Id" = s."TenantId"
                WHERE s."Id" = d."SessionId" AND s."TenantId" = d."TenantId" AND s."UserId" = d."UserId"
                  AND s."RevokedAt" IS NULL AND s."ExpiresAt" > {now} AND s."SecurityStamp" = u."SecurityStamp"
                  AND u."IsActive" AND u."EmailConfirmed" AND u."PasswordHash" IS NOT NULL AND u."PasswordHash" <> ''
                  AND o."IsActive" AND m."IsActive" AND m."Role" = 'Technician'))
            """, cancellationToken);

    private async Task CleanAbandonedAttachmentAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var abandoned = await db.WorkOrderAttachments.FromSqlInterpolated($"""
            SELECT * FROM "WorkOrderAttachments" WHERE NOT "Ready" AND "CreatedAt" < {now.AddHours(-24)}
            ORDER BY "CreatedAt" LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        if (abandoned.SingleOrDefault() is not { } row) return;
        try { await storage.DeleteAsync(row.ObjectKey(), cancellationToken); }
        catch (FieldOps.Application.Common.FieldOpsException)
        {
            logger.LogWarning("Abandoned attachment {AttachmentId} is pending cleanup", row.Id);
            return;
        }
        db.WorkOrderAttachments.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    private async Task<bool> DispatchChangeAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var claimTime = clock.GetUtcNow();
        var batch = await db.WorkOrderEvents.FromSqlInterpolated($"""
            SELECT * FROM "WorkOrderEvents" WHERE "DispatchedAt" IS NULL AND "FailedAt" IS NULL
                AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= {claimTime})
            ORDER BY "CreatedAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        if (batch.SingleOrDefault() is not { } row) return false;
        using var dispatch = FieldOpsTelemetry.StartDelivery(row.TraceParent);
        // Auth checks and dispatch share the lock used by assignment and membership writes.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1 FROM "Organizations" WHERE "Id" = {row.TenantId} FOR UPDATE
            """, cancellationToken);
        if (row.Kind is "scheduled" or "rescheduled" or "cancel" or "complete" or "pause" or "resume" or "updated")
        {
            // Only the latest event for this order can produce a current operational alert.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "InboxNotifications" ("Id", "TenantId", "UserId", "WorkOrderId", "Version", "Kind", "CreatedAt")
                SELECT gen_random_uuid(), a."TenantId", a."TechnicianUserId", a."WorkOrderId", {row.Version}, {row.Kind}, {row.CreatedAt}
                FROM "WorkOrderAssignments" a
                JOIN "WorkOrders" w ON w."TenantId" = a."TenantId" AND w."Id" = a."WorkOrderId"
                WHERE a."TenantId" = {row.TenantId} AND a."WorkOrderId" = {row.WorkOrderId} AND a."RemovedAt" IS NULL
                    AND w."Version" = {row.Version}
                ON CONFLICT DO NOTHING
                """, cancellationToken);
        }
        using var deliveryDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deliveryDeadline.CancelAfter(TimeSpan.FromSeconds(3));
        var outcome = "ok";
        DateTimeOffset transitionedAt;
        try
        {
            await realtime.DeliverAsync(row.TenantId, new(row.Id, row.WorkOrderId, row.Version, row.Kind), row.FormerAssignees, deliveryDeadline.Token);
            transitionedAt = TransitionTime(row.CreatedAt, claimTime);
            row.DispatchedAt = transitionedAt;
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            transitionedAt = TransitionTime(row.CreatedAt, claimTime);
            row.Attempts++;
            if (row.Attempts >= 8) row.FailedAt = transitionedAt;
            else row.NextAttemptAt = transitionedAt.AddSeconds(5 * Math.Pow(2, row.Attempts - 1));
            outcome = row.FailedAt is null ? "retry" : "dead_letter";
            dispatch?.SetStatus(ActivityStatusCode.Error);
            logger.LogWarning("Outbox delivery {EventId} returned {Outcome} with {FailureType}", row.Id, outcome, error.GetType().Name);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        FieldOpsTelemetry.Record("outbox.dispatch", outcome, (transitionedAt - row.CreatedAt).TotalMilliseconds);
        db.ChangeTracker.Clear();
        return true;
    }

    private DateTimeOffset TransitionTime(DateTimeOffset createdAt, DateTimeOffset claimTime)
    {
        // Keep persisted timestamps monotonic if the application wall clock is corrected during delivery.
        var earliest = createdAt > claimTime ? createdAt : claimTime;
        var current = clock.GetUtcNow();
        return current < earliest ? earliest : current;
    }

    private Task<int> InsertUpcomingAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "InboxNotifications" ("Id", "TenantId", "UserId", "WorkOrderId", "Version", "Kind", "CreatedAt")
            SELECT gen_random_uuid(), a."TenantId", a."TechnicianUserId", a."WorkOrderId", w."Version", 'upcoming', {now}
            FROM "WorkOrderAssignments" a
            JOIN "WorkOrders" w ON w."TenantId" = a."TenantId" AND w."Id" = a."WorkOrderId"
            WHERE a."RemovedAt" IS NULL AND w."PlannedStartAt" > {now} AND w."PlannedStartAt" <= {now.AddMinutes(30)}
                AND w."Status" = 'Assigned'
                AND EXISTS (SELECT 1 FROM "OrganizationMemberships" m WHERE m."TenantId" = a."TenantId"
                    AND m."UserId" = a."TechnicianUserId" AND m."IsActive" AND m."Role" = 'Technician')
                AND NOT EXISTS (SELECT 1 FROM "InboxNotifications" i WHERE i."TenantId"=a."TenantId" AND i."UserId"=a."TechnicianUserId"
                    AND i."WorkOrderId"=w."Id" AND i."Kind"='upcoming' AND i."CreatedAt" >= w."PlannedStartAt" - interval '30 minutes')
            ON CONFLICT DO NOTHING
            """, cancellationToken);

    private Task<int> QueuePushAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PushDeliveries" ("Id", "TenantId", "NotificationId", "DeviceId", "SessionId", "TokenHash", "CreatedAt", "DueAt", "Attempts")
            SELECT gen_random_uuid(), n."TenantId", n."Id", d."DeviceId", d."SessionId", d."TokenHash", {now}, {now}, 0
            FROM "InboxNotifications" n
            JOIN "PushDevices" d ON d."TenantId" = n."TenantId" AND d."UserId" = n."UserId"
            WHERE n."CreatedAt" > {now.AddHours(-12)} AND n."ReadAt" IS NULL AND d."RevokedAt" IS NULL
                AND d."LastSeenAt" > {now.AddDays(-30)}
                AND NOT EXISTS (SELECT 1 FROM "PushDeliveries" p WHERE p."TenantId"=n."TenantId" AND p."NotificationId"=n."Id"
                  AND p."DeviceId"=d."DeviceId" AND p."SessionId"=d."SessionId")
            LIMIT 100 ON CONFLICT DO NOTHING
            """, cancellationToken);

    private async Task<bool> DispatchPushAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var batch = await db.PushDeliveries.FromSqlInterpolated($"""
            SELECT * FROM "PushDeliveries" WHERE "CompletedAt" IS NULL AND "DueAt" <= {now}
            ORDER BY "DueAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        if (batch.SingleOrDefault() is not { } row) return false;
        // Coordinate with session revocation and workspace changes before calling a provider.
        var organizations = await db.Organizations.FromSqlInterpolated($"""
            SELECT * FROM "Organizations" WHERE "Id" = {row.TenantId} FOR UPDATE
            """).AsNoTracking().ToListAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1 FROM "AuthSessions" WHERE "Id" = {row.SessionId} AND "TenantId" = {row.TenantId} FOR UPDATE
            """, cancellationToken);
        var devices = await db.PushDevices.FromSqlInterpolated($"""
            SELECT * FROM "PushDevices" WHERE "TenantId" = {row.TenantId} AND "DeviceId" = {row.DeviceId}
                AND "SessionId" = {row.SessionId} FOR UPDATE
            """).AsNoTracking().ToListAsync(cancellationToken);
        var device = devices.SingleOrDefault();
        var notification = await db.InboxNotifications.FindAsync([row.NotificationId], cancellationToken);
        var actor = organizations.Count == 1 && notification is not null && device is not null && device.UserId == notification.UserId &&
            device.RevokedAt is null
            ? await access.ValidateAsync(new(device.UserId, device.SessionId, device.TenantId, OrganizationRole.Technician), cancellationToken)
            : null;
        if (row.CreatedAt < now.AddHours(-23) || notification is null || notification.ReadAt is not null ||
            actor?.Role != OrganizationRole.Technician ||
            notification.Kind != "unassigned" && !await access.CanReadOrderAsync(actor, notification.WorkOrderId, cancellationToken))
        {
            row.CompletedAt = now;
            row.Outcome = "no_longer_eligible";
        }
        else
        {
            var eligibleDevice = device!;
            // Unsent deliveries follow an active installation. Receipts stay bound to the token Expo received.
            if (row.TicketId is null) row.TokenHash = eligibleDevice.TokenHash;
            await AttemptAsync(row, eligibleDevice, now, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return true;
    }

    private async Task AttemptAsync(PushDelivery row, PushDevice device, DateTimeOffset now, CancellationToken cancellationToken)
    {
        using var delivery = FieldOpsTelemetry.Activities.StartActivity("push.delivery", ActivityKind.Client);
        var started = Stopwatch.GetTimestamp();
        var awaitingReceipt = row.TicketId is not null;
        PushResult result;
        try
        {
            result = row.TicketId is { } ticket ? await push.ReceiptAsync(ticket, cancellationToken) :
                await push.SendAsync(protection.CreateProtector("FieldOps.PushDevice.v1").Unprotect(device.ProtectedToken),
                    row.NotificationId, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException &&
            !cancellationToken.IsCancellationRequested)
        {
            result = new(null, "provider_unavailable", true);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            result = new(null, "token_unavailable", false);
        }
        if (result.Error is null && !awaitingReceipt && result.TicketId is not null)
        {
            row.TicketId = result.TicketId;
            row.Outcome = null;
            row.DueAt = now.AddMinutes(15);
            FieldOpsTelemetry.Record("push.delivery", "ticket", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return;
        }
        var rotated = awaitingReceipt && device.TokenHash != row.TokenHash;
        var invalidToken = result.Error == "device_not_registered";
        if (invalidToken && !rotated)
        {
            await db.PushDevices.Where(current => current.TenantId == row.TenantId && current.DeviceId == row.DeviceId &&
                    current.SessionId == row.SessionId && current.TokenHash == row.TokenHash)
                .ExecuteUpdateAsync(setters => setters.SetProperty(current => current.RevokedAt, now)
                    .SetProperty(current => current.ProtectedToken, ""), cancellationToken);
        }
        var resend = awaitingReceipt && (result.Error == "provider_rate_limited" || invalidToken && rotated);
        row.Outcome = result.Error ?? (awaitingReceipt ? "delivered_to_provider" : "provider_ticket_missing");
        if (result.Error is not null) row.Attempts++;
        if ((result.Retryable || resend) && row.Attempts < 8)
        {
            // Pending or unavailable receipts keep their ticket. A definitive provider rejection needs a new send.
            if (resend) row.TicketId = null;
            row.DueAt = now.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, row.Attempts - 1)));
        }
        else
            row.CompletedAt = now;
        if (result.Error is not null)
            logger.LogWarning("Push delivery {DeliveryId} recorded {Outcome}", row.Id, row.Outcome);
        var outcome = result.Error is null ? "ok" : row.CompletedAt is null ? "retry" : "failed";
        FieldOpsTelemetry.Record("push.delivery", outcome, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        if (result.Error is not null) delivery?.SetStatus(ActivityStatusCode.Error);
    }
}

internal sealed class CollaborationWorker(IServiceScopeFactory scopes, ILogger<CollaborationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var maintainedAt = Stopwatch.GetTimestamp();
        var sampleBacklogAt = Stopwatch.GetTimestamp();
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromMilliseconds(500);
            using var activity = FieldOpsTelemetry.Activities.StartActivity("collaboration.worker");
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<CollaborationDispatcher>();
                if (await dispatcher.DispatchChangesAsync(stoppingToken)) delay = TimeSpan.FromMilliseconds(50);
                if (Stopwatch.GetElapsedTime(maintainedAt) >= TimeSpan.FromSeconds(3))
                {
                    await dispatcher.MaintainAsync(stoppingToken);
                    maintainedAt = Stopwatch.GetTimestamp();
                }
                if (Stopwatch.GetElapsedTime(sampleBacklogAt) >= TimeSpan.FromSeconds(30))
                {
                    await dispatcher.ObserveBacklogAsync(stoppingToken);
                    sampleBacklogAt = Stopwatch.GetTimestamp();
                }
                FieldOpsTelemetry.Record("collaboration.worker", "ok");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                delay = TimeSpan.FromSeconds(3);
                activity?.SetStatus(ActivityStatusCode.Error);
                FieldOpsTelemetry.Record("collaboration.worker", "failed");
                logger.LogWarning("Collaboration dispatch failed with {FailureType}", error.GetType().Name);
            }
            await Task.Delay(delay, stoppingToken);
        }
    }
}
