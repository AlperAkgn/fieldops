using FieldOps.Application.Tenancy;
using FieldOps.Domain.Identity;
using FieldOps.Domain.Organizations;

namespace FieldOps.UnitTests;

public sealed class SessionDomainTests
{
    [Fact]
    public void RevocationPreservesTheFirstSecurityReasonAndTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var session = AuthSession.Create(Guid.NewGuid(), Guid.NewGuid(), "Native", "test-security-stamp", now, now.AddDays(7));
        session.Revoke(now.AddMinutes(1), "refresh_reuse");
        session.Revoke(now.AddMinutes(2), "logout");

        Assert.Equal(now.AddMinutes(1), session.RevokedAt);
        Assert.Equal("refresh_reuse", session.RevocationReason);
    }

    [Fact]
    public void RefreshCredentialCannotBeConsumedTwice()
    {
        var now = DateTimeOffset.UtcNow;
        var session = AuthSession.Create(Guid.NewGuid(), Guid.NewGuid(), "Native", "test-security-stamp", now, now.AddDays(7));
        var credential = RefreshCredential.Create(session, new string('A', 64), now, now.AddDays(7));
        var replacement = Guid.NewGuid();
        credential.Consume(replacement, now.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => credential.Consume(Guid.NewGuid(), now.AddMinutes(2)));
        Assert.Equal(replacement, credential.ReplacedById);
        Assert.Equal(now.AddMinutes(1), credential.ConsumedAt);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("session")]
    [InlineData("tenant")]
    [InlineData("role")]
    public void TenantContextRejectsIncompleteValidatedActor(string missing)
    {
        var actor = new TenantActor(missing == "user" ? Guid.Empty : Guid.NewGuid(),
            missing == "session" ? Guid.Empty : Guid.NewGuid(), missing == "tenant" ? Guid.Empty : Guid.NewGuid(),
            missing == "role" ? (OrganizationRole)99 : OrganizationRole.Viewer);
        var context = new TenantContext();
        Assert.Throws<ArgumentException>(() => context.SetValidated(actor));
        Assert.Null(context.Actor);
        Assert.Equal(Guid.Empty, context.CurrentTenantId);
    }
}
