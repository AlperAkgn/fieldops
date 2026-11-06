namespace FieldOps.Infrastructure.Email;

public sealed class OutboundEmail
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ToEmail { get; set; } = "";
    public string Subject { get; set; } = "";
    public string ProtectedBody { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public int Attempts { get; set; }
    public bool IsDeadLetter { get; set; }
    public string? LastFailureType { get; set; }
}
