namespace FieldOps.Application.Collaboration;

public sealed record PushResult(string? TicketId, string? Error, bool Retryable);

public interface IPushProvider
{
    bool Enabled { get; }
    Task<PushResult> SendAsync(string token, Guid notificationId, CancellationToken cancellationToken);
    Task<PushResult> ReceiptAsync(string ticketId, CancellationToken cancellationToken);
}
