using System.Net;
using System.Text;
using System.Text.Json;
using FieldOps.Application.Collaboration;
using FieldOps.Application.Identity;
using FieldOps.Domain.Organizations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace FieldOps.IntegrationTests;

[Trait("Category", "Database")]
public sealed class CollaborationTests : CoreOperationsTestBase
{
    private readonly TestPush push = new();
    private readonly TestStorage storage = new();
    protected override void ConfigureCollaborators(IServiceCollection services)
    {
        services.RemoveAll<IPushProvider>();
        services.RemoveAll<IAttachmentStorage>();
        services.AddSingleton<IPushProvider>(push);
        services.AddSingleton<IAttachmentStorage>(storage);
    }

    [Fact]
    public async Task HubNegotiationRequiresCurrentAuthenticatedSession()
    {
        const string route = "/api/v1/live/negotiate?negotiateVersion=1";
        using var anonymous = await Client.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var owner = await RegisterAsync();
        using var allowed = await AuthorizedAsync(HttpMethod.Post, route, owner.Tokens);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        await CallAsync(HttpMethod.Post, "/auth/logout", owner.Tokens, expected: HttpStatusCode.NoContent);
        using var revoked = await AuthorizedAsync(HttpMethod.Post, route, owner.Tokens);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task ChangesAndNotificationRecipientsCommitWithAssignmentAndAudit()
    {
        var owner = await RegisterAsync();
        var technician = await RegisterAsync();
        var actor = await JoinAsync(owner, technician, OrganizationRole.Technician);
        var outside = await RegisterAsync();
        var order = await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, await CatalogAsync(owner.Tokens)));
        await AssignAsync(owner.Tokens, order, actor.UserId);

        var inbox = await CallAsync(HttpMethod.Get, "/notifications", actor);
        var message = Assert.Single(inbox["items"]!.AsArray())!;
        Assert.Equal(Id(order), message["workOrderId"]!.GetValue<Guid>());
        Assert.Equal("assigned", message["kind"]!.GetValue<string>());
        Assert.Empty((await CallAsync(HttpMethod.Get, "/notifications", outside.Tokens))["items"]!.AsArray());
        await CallAsync(HttpMethod.Post, $"/notifications/{Id(message)}/read", outside.Tokens, expected: HttpStatusCode.NotFound);
        await CallAsync(HttpMethod.Post, $"/notifications/{Id(message)}/read", actor, expected: HttpStatusCode.NoContent);
        Assert.NotNull((await CallAsync(HttpMethod.Get, "/notifications", actor))["items"]![0]!["readAt"]);

        await using var connection = new NpgsqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand("""
            SELECT count(*) FROM "WorkOrderEvents" WHERE "WorkOrderId" = @order
            """, connection);
        count.Parameters.AddWithValue("order", Id(order));
        Assert.Equal(3L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task CriticalOrderAlertsOnlyActiveWorkspaceManagers()
    {
        var owner = await RegisterAsync();
        var manager = await JoinAsync(owner, await RegisterAsync(), OrganizationRole.Manager);
        var viewer = await JoinAsync(owner, await RegisterAsync(), OrganizationRole.Viewer);
        var catalog = await CatalogAsync(owner.Tokens);
        var order = await CallAsync(HttpMethod.Post, "/work-orders", owner.Tokens, new
        {
            catalog.CustomerId,
            ServiceLocationId = catalog.LocationId,
            Title = "Critical inspection",
            Priority = "Critical"
        }, HttpStatusCode.Created);
        foreach (var recipient in new[] { owner.Tokens, manager })
        {
            var inbox = await CallAsync(HttpMethod.Get, "/notifications", recipient);
            var row = Assert.Single(inbox["items"]!.AsArray())!;
            Assert.Equal("critical_order_created", row["kind"]!.GetValue<string>());
            Assert.Equal(Id(order), row["workOrderId"]!.GetValue<Guid>());
        }
        Assert.Empty((await CallAsync(HttpMethod.Get, "/notifications", viewer))["items"]!.AsArray());
    }

    [Fact]
    public async Task LiveSocketOnlyReceivesCommittedAuthorizedEventsAndReconnects()
    {
        var owner = await RegisterAsync();
        var technician = await RegisterAsync();
        var actor = await JoinAsync(owner, technician, OrganizationRole.Technician);
        var other = await RegisterAsync();
        var replacement = await JoinAsync(owner, other, OrganizationRole.Technician);
        var outside = await RegisterAsync();
        var order = await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, await CatalogAsync(owner.Tokens)));
        await DispatchAsync();
        using var ownerSocket = await ConnectAsync(owner.Tokens);
        using var techSocket = await ConnectAsync(actor);
        using var otherSocket = await ConnectAsync(outside.Tokens);
        var failed = await CallAsync(HttpMethod.Post, $"/work-orders/{Id(order)}/assign", owner.Tokens,
            new { expectedVersion = Version(order) - 1, technicianUserIds = new[] { actor.UserId }, primaryTechnicianUserId = actor.UserId },
            HttpStatusCode.Conflict);
        Assert.Equal("work_order_version_conflict", failed["code"]!.GetValue<string>());
        Assert.Empty((await CallAsync(HttpMethod.Get, "/notifications", actor))["items"]!.AsArray());
        var assigned = await AssignAsync(owner.Tokens, order, actor.UserId);
        await DispatchAsync();
        var message = await ReadEventAsync(techSocket);
        Assert.Equal("updated", message.GetProperty("kind").GetString());
        Assert.Equal(Id(order), message.GetProperty("workOrderId").GetGuid());
        Assert.Equal(message.GetProperty("eventId").GetGuid(), (await ReadEventAsync(ownerSocket)).GetProperty("eventId").GetGuid());

        await AssignAsync(owner.Tokens, assigned, replacement.UserId);
        await DispatchAsync();
        var lost = await ReadEventAsync(techSocket);
        Assert.Equal("access_changed", lost.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, lost.GetProperty("workOrderId").ValueKind);
        await CallAsync(HttpMethod.Get, $"/work-orders/{Id(order)}", actor, expected: HttpStatusCode.NotFound);
        var oldNotifications = await CallAsync(HttpMethod.Get, "/notifications", actor);
        Assert.All(oldNotifications["items"]!.AsArray(), row => Assert.Null(row!["workOrderId"]));
        using var absent = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await ReadEventAsync(otherSocket, absent.Token));
        using var reconnected = await ConnectAsync(replacement);
        var current = await CallAsync(HttpMethod.Get, $"/work-orders/{Id(order)}", replacement);
        Assert.True(Version(current["order"]!) > Version(assigned));
    }

    private async Task DispatchAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICollaborationDispatcher>().DispatchAsync(CancellationToken.None);
    }

    private async Task<System.Net.WebSockets.WebSocket> ConnectAsync(TokenPair actor)
    {
        var client = Factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {actor.AccessToken}";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var socket = await client.ConnectAsync(new Uri("wss://localhost/api/v1/live"), deadline.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e").AsMemory(),
            System.Net.WebSockets.WebSocketMessageType.Text, true, deadline.Token);
        var result = await ReadFrameAsync(socket, deadline.Token);
        Assert.Equal("{}", result);
        return socket;
    }

    private static async Task<JsonElement> ReadEventAsync(System.Net.WebSockets.WebSocket socket, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while (true)
        {
            var frame = await ReadFrameAsync(socket, deadline.Token);
            using var json = JsonDocument.Parse(frame);
            if (json.RootElement.TryGetProperty("target", out var target) && target.GetString() == "OrderChanged")
                return json.RootElement.GetProperty("arguments")[0].Clone();
        }
    }

    private static async Task<string> ReadFrameAsync(System.Net.WebSockets.WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
        Assert.True(result.EndOfMessage);
        return Encoding.UTF8.GetString(buffer, 0, result.Count).TrimEnd('\u001e');
    }

    [Fact]
    public async Task AttachmentStorageFailureCanBeRetriedWithoutExposingMetadata()
    {
        var owner = await RegisterAsync();
        var order = await OrderAsync(owner.Tokens, await CatalogAsync(owner.Tokens));
        var route = $"{Root}/work-orders/{Id(order)}/attachments";
        using var invalid = new HttpRequestMessage(HttpMethod.Post, route);
        invalid.Headers.Authorization = new("Bearer", owner.Tokens.AccessToken);
        invalid.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        invalid.Headers.Add("X-File-Name", "visit.jpg");
        invalid.Content = new ByteArrayContent([1, 2, 3]);
        invalid.Content.Headers.ContentType = new("image/jpeg");
        using var response = await Client.SendAsync(invalid);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await CallAsync(HttpMethod.Get, $"/work-orders/{Id(order)}/attachments", owner.Tokens)).AsArray());

        var key = Guid.NewGuid();
        byte[] bytes = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0];
        async Task<HttpResponseMessage> UploadAsync(string filename, byte[] payload, Guid? operationId = null)
        {
            using var upload = new HttpRequestMessage(HttpMethod.Post, route);
            upload.Headers.Authorization = new("Bearer", owner.Tokens.AccessToken);
            upload.Headers.Add("Idempotency-Key", (operationId ?? key).ToString());
            upload.Headers.Add("X-File-Name", filename);
            upload.Content = new ByteArrayContent(payload);
            upload.Content.Headers.ContentType = new("image/png");
            return await Client.SendAsync(upload);
        }
        using var unsafeName = await UploadAsync("..%2Fvisit.png", bytes);
        Assert.Equal(HttpStatusCode.BadRequest, unsafeName.StatusCode);
        using var oversized = await UploadAsync("visit.png", new byte[IAttachments.MaxBytes + 1]);
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        storage.Fail = true;
        using var unavailable = await UploadAsync("visit.png", bytes);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Empty((await CallAsync(HttpMethod.Get, $"/work-orders/{Id(order)}/attachments", owner.Tokens)).AsArray());
        storage.Fail = false;
        using var success = await UploadAsync("visit.png", bytes);
        Assert.Equal(HttpStatusCode.Created, success.StatusCode);
        var file = await ReadJsonAsync(success);
        using var repeat = await UploadAsync("visit.png", bytes);
        Assert.Equal(Id(file), Id(await ReadJsonAsync(repeat)));
        Assert.Single(storage.Objects);
        var download = $"{route}/{Id(file)}";
        using var allowed = await AuthorizedAsync(HttpMethod.Get, download, owner.Tokens);
        Assert.Equal(bytes, await allowed.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", Assert.Single(allowed.Headers.GetValues("X-Content-Type-Options")));
        var outside = await RegisterAsync();
        using var denied = await AuthorizedAsync(HttpMethod.Get, download, outside.Tokens);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        storage.FailAfterWrite = true;
        using var interrupted = await UploadAsync("interrupted.png", bytes, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, interrupted.StatusCode);
        Assert.Equal(2, storage.Objects.Count);
        var visible = await CallAsync(HttpMethod.Get, $"/work-orders/{Id(order)}/attachments", owner.Tokens);
        Assert.Equal(Id(file), Id(Assert.Single(visible.AsArray())!));
        await using var connection = new NpgsqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();
        await using var age = new NpgsqlCommand("""
            UPDATE "WorkOrderAttachments" SET "CreatedAt" = now() - interval '25 hours' WHERE NOT "Ready"
            """, connection);
        Assert.Equal(1, await age.ExecuteNonQueryAsync());
        await DispatchAsync();
        Assert.Single(storage.Objects);
        await using var remaining = new NpgsqlCommand("""
            SELECT count(*) FROM "WorkOrderAttachments" WHERE NOT "Ready"
            """, connection);
        Assert.Equal(0L, await remaining.ExecuteScalarAsync());
        using var kept = await AuthorizedAsync(HttpMethod.Get, download, owner.Tokens);
        Assert.Equal(bytes, await kept.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task PushDeduplicatesAndStopsUsingRevokedSessionsAndInvalidTokens()
    {
        var owner = await RegisterAsync();
        var technician = await RegisterAsync();
        var actor = await JoinAsync(owner, technician, OrganizationRole.Technician);
        var catalog = await CatalogAsync(owner.Tokens);
        var path = $"/notifications/devices/{Guid.NewGuid()}";
        var token = new { Token = "ExpoPushToken[fixture-token]" };
        await CallAsync(HttpMethod.Put, path, owner.Tokens, token, HttpStatusCode.Forbidden);
        await CallAsync(HttpMethod.Put, path, actor, token, HttpStatusCode.NoContent);
        await CallAsync(HttpMethod.Put, path, actor, token, HttpStatusCode.NoContent);
        await AssignAsync(owner.Tokens, await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog)), actor.UserId);
        await DispatchAsync();
        await DispatchAsync();
        Assert.Equal(1, push.SendCount);
        await using var connection = new NpgsqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();
        await using var due = new NpgsqlCommand("""UPDATE "PushDeliveries" SET "DueAt" = now() WHERE "CompletedAt" IS NULL""", connection);
        await due.ExecuteNonQueryAsync();
        await DispatchAsync();
        Assert.Equal(1, push.ReceiptCount);

        await CallAsync(HttpMethod.Put, path, actor, token, HttpStatusCode.NoContent);
        await CallAsync(HttpMethod.Post, "/auth/logout", actor, expected: HttpStatusCode.NoContent);
        await AssignAsync(owner.Tokens, await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog),
            "2027-01-10T11:00:00Z", "2027-01-10T12:00:00Z"), actor.UserId);
        await DispatchAsync();
        Assert.Equal(1, push.SendCount);
        await CallAsync(HttpMethod.Delete, path, actor, expected: HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpcomingReminderIsDeduplicatedAndProviderFailureDoesNotAffectTheOrder()
    {
        var owner = await RegisterAsync();
        var actor = await JoinAsync(owner, await RegisterAsync(), OrganizationRole.Technician);
        var path = $"/notifications/devices/{Guid.NewGuid()}";
        await CallAsync(HttpMethod.Put, path, actor, new { Token = "ExpoPushToken[upcoming-fixture]" }, HttpStatusCode.NoContent);
        var start = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).AddMinutes(10);
        var order = await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, await CatalogAsync(owner.Tokens)),
            start.ToString("O"), start.AddHours(1).ToString("O"));
        var assigned = await AssignAsync(owner.Tokens, order, actor.UserId);
        push.RetryableFailure = true;
        await DispatchAsync();
        await DispatchAsync();
        Assert.Equal(2, push.SendCount);
        var notifications = await CallAsync(HttpMethod.Get, "/notifications", actor);
        Assert.Single(notifications["items"]!.AsArray(), row => row!["kind"]!.GetValue<string>() == "upcoming");
        var detail = await CallAsync(HttpMethod.Get, $"/work-orders/{Id(order)}", actor);
        Assert.Equal(Version(assigned), Version(detail["order"]!));
        await CallAsync(HttpMethod.Delete, path, actor, expected: HttpStatusCode.NoContent);
        await DispatchAsync();
        Assert.Equal(2, push.SendCount);
    }

    [Fact]
    public async Task RotationRetriesUnsentPushAndLateReceiptDoesNotRevokeReplacement()
    {
        var owner = await RegisterAsync();
        var actor = await JoinAsync(owner, await RegisterAsync(), OrganizationRole.Technician);
        var path = $"/notifications/devices/{Guid.NewGuid()}";
        const string original = "ExpoPushToken[original-device]";
        const string rotated = "ExpoPushToken[rotated-device]";
        const string replacement = "ExpoPushToken[replacement-device]";
        await CallAsync(HttpMethod.Put, path, actor, new { Token = original }, HttpStatusCode.NoContent);
        var catalog = await CatalogAsync(owner.Tokens);
        await AssignAsync(owner.Tokens, await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog)), actor.UserId);
        push.RetryableFailure = true;
        await DispatchAsync();
        Assert.Equal([original], push.Tokens);

        await CallAsync(HttpMethod.Put, path, actor, new { Token = rotated }, HttpStatusCode.NoContent);
        await using var connection = new NpgsqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();
        await using var due = new NpgsqlCommand("""UPDATE "PushDeliveries" SET "DueAt" = now() WHERE "CompletedAt" IS NULL""", connection);
        await due.ExecuteNonQueryAsync();
        push.RetryableFailure = false;
        await DispatchAsync();
        Assert.Equal([original, rotated], push.Tokens);

        await CallAsync(HttpMethod.Put, path, actor, new { Token = replacement }, HttpStatusCode.NoContent);
        await due.ExecuteNonQueryAsync();
        await DispatchAsync();
        Assert.Equal(1, push.ReceiptCount);
        Assert.Equal([original, rotated], push.Tokens);
        await due.ExecuteNonQueryAsync();
        await DispatchAsync();
        Assert.Equal([original, rotated, replacement], push.Tokens);
        Assert.Single(push.NotificationIds.Distinct());
        var next = await ScheduleAsync(owner.Tokens, await OrderAsync(owner.Tokens, catalog),
            "2027-01-10T11:00:00Z", "2027-01-10T12:00:00Z");
        await AssignAsync(owner.Tokens, next, actor.UserId);
        await DispatchAsync();
        Assert.Equal([original, rotated, replacement, replacement], push.Tokens);
        Assert.Equal(2, push.NotificationIds.Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RateLimitedReceiptsResendTheSameNotificationWithBackoffAndAnOverallLimit(bool revoke)
    {
        var owner = await RegisterAsync();
        var actor = await JoinAsync(owner, await RegisterAsync(), OrganizationRole.Technician);
        var path = $"/notifications/devices/{Guid.NewGuid()}";
        await CallAsync(HttpMethod.Put, path, actor, new { Token = "ExpoPushToken[rate-limit]" }, HttpStatusCode.NoContent);
        await AssignAsync(owner.Tokens, await ScheduleAsync(owner.Tokens,
            await OrderAsync(owner.Tokens, await CatalogAsync(owner.Tokens))), actor.UserId);
        push.ReceiptResult = new(null, "provider_rate_limited", true);
        await DispatchAsync();
        await using var connection = new NpgsqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();
        await using var due = new NpgsqlCommand("""UPDATE "PushDeliveries" SET "DueAt" = now() WHERE "CompletedAt" IS NULL""", connection);
        await due.ExecuteNonQueryAsync();
        await DispatchAsync();
        await DispatchAsync();
        Assert.Equal(1, push.SendCount);
        Assert.Single(push.Tickets);
        await using var queued = new NpgsqlCommand("""
            SELECT count(*) FROM "PushDeliveries" WHERE "CompletedAt" IS NULL AND "TicketId" IS NULL
                AND "Outcome" = 'provider_rate_limited' AND "DueAt" > now()
            """, connection);
        Assert.Equal(1L, await queued.ExecuteScalarAsync());
        if (revoke) await CallAsync(HttpMethod.Post, "/auth/logout", actor, expected: HttpStatusCode.NoContent);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await due.ExecuteNonQueryAsync();
            await DispatchAsync();
        }
        Assert.Equal(revoke ? 1 : 8, push.SendCount);
        Assert.Equal(push.SendCount, push.Tickets.Count);
        Assert.Equal(push.Tickets.Count, push.Tickets.Distinct().Count());
        Assert.Single(push.NotificationIds.Distinct());
        await using var delivery = new NpgsqlCommand("""
            SELECT "Outcome" FROM "PushDeliveries" WHERE "CompletedAt" IS NOT NULL
            """, connection);
        Assert.Equal(revoke ? "no_longer_eligible" : "provider_rate_limited", await delivery.ExecuteScalarAsync());
    }

    private sealed class TestPush : IPushProvider
    {
        public bool Enabled => true;
        public int SendCount { get; private set; }
        public int ReceiptCount { get; private set; }
        public bool RetryableFailure { get; set; }
        public List<string> Tokens { get; } = [];
        public List<Guid> NotificationIds { get; } = [];
        public List<string> Tickets { get; } = [];
        public PushResult ReceiptResult { get; set; } = new(null, "device_not_registered", false);
        public Task<PushResult> SendAsync(string token, Guid notificationId, CancellationToken cancellationToken)
        {
            SendCount++;
            Tokens.Add(token);
            NotificationIds.Add(notificationId);
            return Task.FromResult(RetryableFailure ? new PushResult(null, "provider_unavailable", true) : new PushResult($"fixture-ticket-{SendCount}", null, false));
        }
        public Task<PushResult> ReceiptAsync(string ticketId, CancellationToken cancellationToken)
        {
            ReceiptCount++;
            Tickets.Add(ticketId);
            return Task.FromResult(ReceiptResult);
        }
    }

    private sealed class TestStorage : IAttachmentStorage
    {
        public Dictionary<string, byte[]> Objects { get; } = [];
        public bool Fail { get; set; }
        public bool FailAfterWrite { get; set; }
        public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
        {
            if (Fail) throw StorageUnavailable();
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Objects[key] = buffer.ToArray();
            if (FailAfterWrite) throw StorageUnavailable();
        }
        private static FieldOps.Application.Common.FieldOpsException StorageUnavailable() =>
            new(FieldOps.Application.Common.ErrorKind.Unavailable, "object_storage_unavailable", "Storage unavailable.");
        public Task<(Stream Stream, IDisposable Owner)> GetAsync(string key, CancellationToken cancellationToken)
        {
            var stream = new MemoryStream(Objects[key]);
            return Task.FromResult<(Stream, IDisposable)>((stream, stream));
        }
        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            Objects.Remove(key);
            return Task.CompletedTask;
        }
    }
}
