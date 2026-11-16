namespace FieldOps.Application.Collaboration;

public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, long Size, DateTimeOffset CreatedAt);
public sealed record AttachmentContent(AttachmentDto Attachment, Stream Stream, IDisposable Owner) : IDisposable
{
    public void Dispose() => Owner.Dispose();
}

public interface IAttachments
{
    const int MaxBytes = 8 * 1024 * 1024;
    Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid workOrderId, CancellationToken cancellationToken);
    Task<AttachmentDto> UploadAsync(Guid workOrderId, Guid operationId, string fileName, string? contentType,
        Stream body, CancellationToken cancellationToken);
    Task<AttachmentContent> DownloadAsync(Guid workOrderId, Guid attachmentId, CancellationToken cancellationToken);
}

public interface IAttachmentStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);
    Task<(Stream Stream, IDisposable Owner)> GetAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
