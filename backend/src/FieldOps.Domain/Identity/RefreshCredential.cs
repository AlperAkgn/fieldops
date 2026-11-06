namespace FieldOps.Domain.Identity;

public sealed class RefreshCredential
{
    private RefreshCredential() { }
    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public Guid? ReplacedById { get; private set; }

    public static RefreshCredential Create(AuthSession session, string tokenHash, DateTimeOffset now,
        DateTimeOffset expiresAt) => new()
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            FamilyId = session.FamilyId,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };

    public void Consume(Guid replacementId, DateTimeOffset now)
    {
        if (ConsumedAt is not null) throw new InvalidOperationException("Refresh credential has already been consumed.");
        ConsumedAt = now;
        ReplacedById = replacementId;
    }
}
