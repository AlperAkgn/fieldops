using System.Security.Cryptography;
using FieldOps.Application.Collaboration;
using FieldOps.Application.Common;
using FieldOps.Infrastructure.Operations;
using FieldOps.Infrastructure.Persistence;
using FieldOps.Infrastructure.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Collaboration;

internal sealed class AttachmentService(FieldOpsDbContext db, OperationalWriteContext writes, IAttachmentStorage storage) : IAttachments
{
    public async Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid workOrderId, CancellationToken cancellationToken)
    {
        var visible = WorkOrderAccess.VisibleTo(db, writes.Actor).Where(row => row.Id == workOrderId);
        if (!await visible.AnyAsync(cancellationToken)) throw WorkOrderAccess.NotFound();
        return await db.WorkOrderAttachments.AsNoTracking()
            .Where(row => row.Ready && row.WorkOrderId == workOrderId && visible.Any(order => order.TenantId == row.TenantId))
            .OrderByDescending(row => row.CreatedAt).ThenBy(row => row.Id)
            .Select(row => new AttachmentDto(row.Id, row.FileName, row.ContentType, row.Size, row.CreatedAt)).ToListAsync(cancellationToken);
    }

    public async Task<AttachmentDto> UploadAsync(Guid workOrderId, Guid operationId, string fileName, string? contentType,
        Stream body, CancellationToken cancellationToken)
    {
        var actor = writes.Actor;
        WorkOrderAccess.RequireExecution(actor);
        if (!await WorkOrderAccess.VisibleTo(db, actor).AnyAsync(row => row.Id == workOrderId, cancellationToken))
            throw WorkOrderAccess.NotFound();
        if (operationId == Guid.Empty || !SafeName(fileName))
            throw Invalid("invalid_attachment_name", "Provide a safe filename and upload key.");
        if (contentType is not ("image/jpeg" or "image/png" or "application/pdf"))
            throw Invalid("invalid_attachment_type", "Only JPEG, PNG and PDF files are supported.");

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = 64 * 1024,
            Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var temporary = new FileStream(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), options);
        var hash = await CopyValidatedAsync(body, temporary, contentType, cancellationToken);
        var attachment = await ReserveAsync(workOrderId, operationId, fileName.Trim(), contentType, hash, temporary.Length, cancellationToken);
        await using var transaction = await writes.BeginAsync(cancellationToken);
        var order = await WorkOrderAccess.LoadAsync(db, actor, workOrderId, cancellationToken);
        var matches = await db.WorkOrderAttachments.FromSqlInterpolated($"""
            SELECT * FROM "WorkOrderAttachments"
            WHERE "TenantId" = {actor.TenantId} AND "Id" = {attachment.Id} FOR UPDATE
            """).ToListAsync(cancellationToken);
        var current = matches.SingleOrDefault();
        if (current is null) throw new FieldOpsException(ErrorKind.Conflict, "attachment_retry_required", "Retry the upload.");
        if (!current.Ready)
        {
            temporary.Position = 0;
            await storage.PutAsync(current.ObjectKey(), temporary, contentType, cancellationToken);
            current.Ready = true;
            writes.AttachmentAdded(workOrderId, order.Version);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return ToDto(current);
    }

    private async Task<WorkOrderAttachment> ReserveAsync(Guid workOrderId, Guid operationId, string fileName,
        string contentType, string hash, long size, CancellationToken cancellationToken)
    {
        var actor = writes.Actor;
        await using var transaction = await writes.BeginAsync(cancellationToken);
        await WorkOrderAccess.LoadAsync(db, actor, workOrderId, cancellationToken);
        var current = await db.WorkOrderAttachments.AsNoTracking().SingleOrDefaultAsync(row => row.TenantId == actor.TenantId &&
            row.WorkOrderId == workOrderId && row.UploaderId == actor.UserId && row.OperationId == operationId, cancellationToken);
        if (current is not null && (current.Hash != hash || current.FileName != fileName || current.ContentType != contentType))
            throw new FieldOpsException(ErrorKind.Conflict, "attachment_key_reused", "Use a new upload key for a different file.");
        if (current is null)
        {
            current = new(Guid.NewGuid(), actor.TenantId, workOrderId, actor.UserId, operationId, fileName, contentType, hash,
                size, writes.Now);
            db.WorkOrderAttachments.Add(current);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return current;
    }

    public async Task<AttachmentContent> DownloadAsync(Guid workOrderId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var visible = WorkOrderAccess.VisibleTo(db, writes.Actor).Where(row => row.Id == workOrderId);
        var current = await db.WorkOrderAttachments.AsNoTracking().SingleOrDefaultAsync(row => row.Ready && row.Id == attachmentId &&
            row.WorkOrderId == workOrderId && visible.Any(order => order.TenantId == row.TenantId), cancellationToken)
            ?? throw new FieldOpsException(ErrorKind.NotFound, "attachment_not_found", "The attachment was not found.");
        var content = await storage.GetAsync(current.ObjectKey(), cancellationToken);
        return new(ToDto(current), content.Stream, content.Owner);
    }

    private static bool SafeName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 160 &&
        name.Trim() is not "." and not ".." && !name.Any(character => char.IsControl(character) || character is '/' or '\\' or ':' ||
            char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.Format);

    private static async Task<string> CopyValidatedAsync(Stream source, Stream target, string contentType, CancellationToken cancellationToken)
    {
        using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        var header = new byte[8];
        var headerLength = 0;
        int length;
        while ((length = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (target.Length + length > IAttachments.MaxBytes)
                throw Invalid("attachment_too_large", "Files must be 8 MiB or smaller.");
            var copy = Math.Min(header.Length - headerLength, length);
            Array.Copy(buffer, 0, header, headerLength, copy);
            headerLength += copy;
            checksum.AppendData(buffer, 0, length);
            await target.WriteAsync(buffer.AsMemory(0, length), cancellationToken);
        }
        var valid = headerLength == 8 && contentType switch
        {
            "image/jpeg" => header.AsSpan(0, 3).SequenceEqual(new byte[] { 255, 216, 255 }),
            "image/png" => header.AsSpan().SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "application/pdf" => header.AsSpan().StartsWith("%PDF-"u8),
            _ => false
        };
        if (!valid) throw Invalid("attachment_content_mismatch", "The file content does not match the declared file type.");
        return Convert.ToHexString(checksum.GetHashAndReset());
    }

    private static AttachmentDto ToDto(WorkOrderAttachment row) => new(row.Id, row.FileName, row.ContentType, row.Size, row.CreatedAt);
    private static FieldOpsException Invalid(string code, string message) => new(ErrorKind.Validation, code, message);
}
