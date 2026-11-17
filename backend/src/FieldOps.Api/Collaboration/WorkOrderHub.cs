using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using FieldOps.Application.Collaboration;
using FieldOps.Application.Common;
using FieldOps.Application.Tenancy;
using FieldOps.Domain.Organizations;
using Microsoft.AspNetCore.SignalR;

namespace FieldOps.Api.Collaboration;

public sealed class WorkOrderHub(ICollaborationAccess access, WorkOrderConnections connections) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var principal = Context.User;
        if (principal is null || !Guid.TryParse(principal.FindFirstValue("sub"), out var userId) ||
            !Guid.TryParse(principal.FindFirstValue("sid"), out var sessionId) ||
            !Guid.TryParse(principal.FindFirstValue("tenant_id"), out var tenantId) ||
            await access.ValidateAsync(new(userId, sessionId, tenantId, default), Context.ConnectionAborted) is not { } actor)
        {
            Context.Abort();
            return;
        }
        connections.Register(Context.ConnectionId, actor);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

public sealed class WorkOrderConnections
{
    internal ConcurrentDictionary<string, TenantActor> Active { get; } = new();

    internal void Register(string id, TenantActor actor)
    {
        if (Active.TryAdd(id, actor)) FieldOpsTelemetry.ConnectionChanged(1);
    }

    internal void Remove(string id)
    {
        if (Active.TryRemove(id, out _)) FieldOpsTelemetry.ConnectionChanged(-1);
    }
}

internal sealed class SignalROrderDelivery(IHubContext<WorkOrderHub> hub, WorkOrderConnections connections,
    ICollaborationAccess access) : IOrderChangeDelivery
{
    public async Task DeliverAsync(Guid tenantId, OrderChanged change, IReadOnlyCollection<Guid> formerAssignees,
        CancellationToken cancellationToken)
    {
        using var activity = FieldOpsTelemetry.Activities.StartActivity("signalr.fanout", ActivityKind.Producer);
        foreach (var (connectionId, connected) in connections.Active)
        {
            if (connected.TenantId != tenantId) continue;
            var actor = await access.ValidateAsync(connected, cancellationToken);
            if (actor is null || actor.Role != connected.Role)
            {
                connections.Remove(connectionId);
                continue;
            }

            if (change.WorkOrderId is { } order && await access.CanReadOrderAsync(actor, order, cancellationToken))
                await hub.Clients.Client(connectionId).SendAsync("OrderChanged", actor.Role is OrganizationRole.OrganizationOwner or OrganizationRole.Manager
                    ? change : change with { Kind = "updated" }, cancellationToken);
            else if (formerAssignees.Contains(actor.UserId))
                await hub.Clients.Client(connectionId).SendAsync("OrderChanged",
                    change with { WorkOrderId = null, Version = 0, Kind = "access_changed" }, cancellationToken);
        }
    }
}
