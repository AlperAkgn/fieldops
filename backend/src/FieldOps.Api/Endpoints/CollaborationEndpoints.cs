using System.Diagnostics;
using FieldOps.Api.Collaboration;
using FieldOps.Application.Collaboration;
using FieldOps.Application.Common;
using FieldOps.Application.Operations;
using Microsoft.AspNetCore.Http.Features;

namespace FieldOps.Api.Endpoints;

public static class CollaborationEndpoints
{
    public static void MapCollaborationEndpoints(this WebApplication app)
    {
        app.MapHub<WorkOrderHub>("/api/v1/live", options => options.CloseOnAuthenticationExpiration = true)
            .RequireAuthorization("OperationalRead").RequireRateLimiting("api");
        var inbox = app.MapGroup("/api/v1/notifications").WithTags("Notifications")
            .RequireAuthorization("OperationalRead").RequireRateLimiting("api").WithProblemResponses(400, 401, 403, 404, 429);
        inbox.MapGet("/", async (int? page, int? pageSize, INotifications notifications, CancellationToken ct) =>
            TypedResults.Ok(await notifications.ListAsync(new PageRequest(page ?? 1, pageSize ?? 25), ct)));
        inbox.MapPost("/{id:guid}/read", async (Guid id, INotifications notifications, CancellationToken ct) =>
        {
            await notifications.MarkReadAsync(id, ct);
            return TypedResults.NoContent();
        });
        inbox.MapPut("/devices/{id:guid}", async (Guid id, RegisterPushDevice request, INotifications notifications, CancellationToken ct) =>
        {
            await notifications.RegisterAsync(id, request, ct);
            return TypedResults.NoContent();
        });
        inbox.MapDelete("/devices/{id:guid}", async (Guid id, INotifications notifications, CancellationToken ct) =>
        {
            await notifications.RevokeAsync(id, ct);
            return TypedResults.NoContent();
        });

        var files = app.MapGroup("/api/v1/work-orders/{id:guid}/attachments").WithTags("Attachments")
            .RequireAuthorization("OperationalRead").RequireRateLimiting("api").WithProblemResponses(400, 401, 403, 404, 409, 429, 503);
        files.MapGet("/", async (Guid id, IAttachments attachments, CancellationToken ct) =>
            TypedResults.Ok(await attachments.ListAsync(id, ct)));
        files.MapPost("/", async (Guid id, HttpContext http, IAttachments attachments, CancellationToken ct) =>
        {
            var max = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (max is { IsReadOnly: false }) max.MaxRequestBodySize = IAttachments.MaxBytes + 1;
            if (http.Request.ContentLength > IAttachments.MaxBytes)
                throw new FieldOpsException(ErrorKind.Validation, "attachment_too_large", "Files must be 8 MiB or smaller.");
            if (!Guid.TryParse(http.Request.Headers["Idempotency-Key"], out var uploadId))
                throw new FieldOpsException(ErrorKind.Validation, "attachment_key_required", "Provide an upload key.");
            var fileName = Uri.UnescapeDataString(http.Request.Headers["X-File-Name"].ToString());
            var result = await attachments.UploadAsync(id, uploadId, fileName, http.Request.ContentType, http.Request.Body, ct);
            return TypedResults.Created($"/api/v1/work-orders/{id}/attachments/{result.Id}", result);
        }).RequireAuthorization("WorkOrderExecute");
        files.MapGet("/{attachmentId:guid}", WriteDownloadAsync).Produces(StatusCodes.Status200OK);
    }

    private static async Task WriteDownloadAsync(Guid id, Guid attachmentId, HttpContext http, IAttachments attachments,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var started = Stopwatch.GetTimestamp();
        var outcome = "failed";
        try
        {
            using var content = await attachments.DownloadAsync(id, attachmentId, deadline.Token);
            http.Response.ContentLength = content.Attachment.Size;
            await TypedResults.Stream(destination => content.Stream.CopyToAsync(destination, deadline.Token),
                content.Attachment.ContentType, content.Attachment.FileName).ExecuteAsync(http);
            outcome = "ok";
        }
        catch (FieldOpsException error)
        {
            outcome = error.Kind == ErrorKind.Unavailable ? "failed" : "rejected";
            throw;
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or HttpRequestException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                outcome = "cancelled";
                return;
            }
            outcome = deadline.IsCancellationRequested ? "timeout" : "failed";
            loggerFactory.CreateLogger(FieldOpsTelemetry.Name).LogWarning("Attachment download ended with {Outcome} ({FailureType})",
                outcome, error.GetType().Name);
            if (!http.Response.HasStarted)
            {
                http.Response.Clear();
                throw new FieldOpsException(ErrorKind.Unavailable, "object_storage_unavailable", "File storage is temporarily unavailable.");
            }
            // Once headers and bytes went out, fail the transport so callers cannot accept a partial file.
            http.Abort();
        }
        finally
        {
            FieldOpsTelemetry.Record("storage.download", outcome, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}
