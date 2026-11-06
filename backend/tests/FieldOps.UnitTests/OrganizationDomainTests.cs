using FieldOps.Domain.Organizations;

namespace FieldOps.UnitTests;

public sealed class OrganizationDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 15, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void OrganizationNormalizesNameAndAuditInstants()
    {
        var organization = Organization.Create("  Acme Field Service  ", "Europe/Istanbul", Now);

        Assert.NotEqual(Guid.Empty, organization.Id);
        Assert.Equal("Acme Field Service", organization.Name);
        Assert.Equal("Europe/Istanbul", organization.TimeZoneId);
        Assert.Equal(TimeSpan.Zero, organization.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, organization.UpdatedAt.Offset);
        Assert.True(organization.IsActive);
    }

    [Theory]
    [InlineData("", "UTC")]
    [InlineData("   ", "UTC")]
    [InlineData("Valid name", "Not/A_Time_Zone")]
    [InlineData("Valid name", "Turkey Standard Time")]
    public void OrganizationRejectsInvalidOrNonPortableSettings(string name, string timeZone)
        => Assert.ThrowsAny<ArgumentException>(() => Organization.Create(name, timeZone, Now));

    [Fact]
    public void OrganizationRejectsOverlongName()
        => Assert.Throws<ArgumentException>(() => Organization.Create(new string('a', 161), "UTC", Now));

    [Fact]
    public void MembershipRequiresValidUserTenantAndRole()
    {
        Assert.Throws<ArgumentException>(() => OrganizationMembership.Create(Guid.Empty, Guid.NewGuid(), OrganizationRole.Manager, Now));
        Assert.Throws<ArgumentException>(() => OrganizationMembership.Create(Guid.NewGuid(), Guid.Empty, OrganizationRole.Manager, Now));
        Assert.Throws<ArgumentException>(() => OrganizationMembership.Create(Guid.NewGuid(), Guid.NewGuid(), (OrganizationRole)99, Now));
    }

    [Fact]
    public void MembershipChangesPreserveItsIdentityAndScope()
    {
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var membership = OrganizationMembership.Create(tenant, user, OrganizationRole.Technician, Now);
        var id = membership.Id;
        membership.ChangeRole(OrganizationRole.Viewer, Now.AddHours(1));
        membership.SetActive(false, Now.AddHours(2));

        Assert.Equal(id, membership.Id);
        Assert.Equal(tenant, membership.TenantId);
        Assert.Equal(user, membership.UserId);
        Assert.Equal(OrganizationRole.Viewer, membership.Role);
        Assert.False(membership.IsActive);
        Assert.Equal(Now.AddHours(2).ToUniversalTime(), membership.UpdatedAt);
    }

    [Fact]
    public void InvitationExpiresAtTheExactBoundary()
    {
        var invitation = NewInvitation();
        Assert.True(invitation.CanAccept(Now.AddHours(1).AddTicks(-1)));
        Assert.False(invitation.CanAccept(Now.AddHours(1)));
        Assert.Throws<InvalidOperationException>(() => invitation.Accept(Guid.NewGuid(), Now.AddHours(1)));
    }

    [Fact]
    public void InvitationIsSingleUseAndCannotBeRevokedAfterAcceptance()
    {
        var invitation = NewInvitation();
        var membership = Guid.NewGuid();
        invitation.Accept(membership, Now.AddMinutes(1));

        Assert.Equal(membership, invitation.AcceptedByMembershipId);
        Assert.False(invitation.CanAccept(Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => invitation.Accept(Guid.NewGuid(), Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => invitation.Revoke(Now.AddMinutes(2)));
    }

    [Fact]
    public void RevokingInvitationIsIdempotentAndPreventsAcceptance()
    {
        var invitation = NewInvitation();
        invitation.Revoke(Now.AddMinutes(1));
        invitation.Revoke(Now.AddMinutes(2));

        Assert.Equal(Now.AddMinutes(1).ToUniversalTime(), invitation.RevokedAt);
        Assert.False(invitation.CanAccept(Now.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => invitation.Accept(Guid.NewGuid(), Now.AddMinutes(3)));
    }

    [Fact]
    public void InvitationRejectsInvalidScopeRoleHashAndLifetime()
    {
        Assert.Throws<ArgumentException>(() => OrganizationInvitation.Create(Guid.Empty, Guid.NewGuid(), "A@EXAMPLE.TEST", OrganizationRole.Viewer, new string('a', 64), Now.AddHours(1), Now));
        Assert.Throws<ArgumentException>(() => OrganizationInvitation.Create(Guid.NewGuid(), Guid.NewGuid(), "A@EXAMPLE.TEST", (OrganizationRole)99, new string('a', 64), Now.AddHours(1), Now));
        Assert.Throws<ArgumentException>(() => OrganizationInvitation.Create(Guid.NewGuid(), Guid.NewGuid(), "A@EXAMPLE.TEST", OrganizationRole.Viewer, "raw-token", Now.AddHours(1), Now));
        Assert.Throws<ArgumentException>(() => OrganizationInvitation.Create(Guid.NewGuid(), Guid.NewGuid(), "A@EXAMPLE.TEST", OrganizationRole.Viewer, new string('a', 64), Now, Now));
    }

    private static OrganizationInvitation NewInvitation() => OrganizationInvitation.Create(Guid.NewGuid(), Guid.NewGuid(),
        "TECHNICIAN@EXAMPLE.TEST", OrganizationRole.Technician, new string('a', 64), Now.AddHours(1), Now);
}
