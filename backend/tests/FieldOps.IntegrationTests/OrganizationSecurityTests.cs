using System.Net;
using System.Net.Http.Json;
using FieldOps.Application.Common;
using FieldOps.Application.Identity;
using FieldOps.Application.Organizations;
using FieldOps.Application.Tenancy;
using FieldOps.Domain.Identity;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FieldOps.IntegrationTests;

[Trait("Category", "Database")]
public sealed class OrganizationSecurityTests : IdentityTestBase
{
    [Fact]
    public async Task OtherTenantIdsCannotExposeOrMutateOrganizationsMembersOrInvitations()
    {
        var first = await RegisterAsync();
        var second = await RegisterAsync();
        var foreignId = second.Tokens.TenantId;
        foreach (var suffix in new[] { "", "/members", "/invitations" })
        {
            using var response = await AuthorizedAsync(HttpMethod.Get, $"/api/v1/organizations/{foreignId}{suffix}", first.Tokens);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var patch = await AuthorizedAsync(HttpMethod.Patch, $"/api/v1/organizations/{foreignId}", first.Tokens,
            new UpdateOrganizationRequest("Stolen name", null));
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
        using var invitation = await AuthorizedAsync(HttpMethod.Post, $"/api/v1/organizations/{foreignId}/invitations", first.Tokens,
            new CreateInvitationRequest(first.Email, OrganizationRole.OrganizationOwner));
        Assert.Equal(HttpStatusCode.NotFound, invitation.StatusCode);
        using var select = await AuthorizedAsync(HttpMethod.Post, $"/api/v1/organizations/{foreignId}/select",
            first.Tokens, new { first.Tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.NotFound, select.StatusCode);
    }

    [Fact]
    public async Task NativeSelectionRequiresCurrentRefreshCredentialFromTheBearerSession()
    {
        var account = await RegisterAsync();
        var previous = account.Tokens;
        var path = $"/api/v1/organizations/{previous.TenantId}/select";
        using var missing = await AuthorizedAsync(HttpMethod.Post, path, previous, new { refreshToken = "" });
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        var otherSession = await LoginAsync(account);
        using var mismatch = await AuthorizedAsync(HttpMethod.Post, path, previous, new { otherSession.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, mismatch.StatusCode);

        using var refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { previous.RefreshToken }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = (await refresh.Content.ReadFromJsonAsync<TokenPair>(JsonOptions))!;
        using var reuse = await AuthorizedAsync(HttpMethod.Post, path, rotated, new { previous.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        using var afterReuse = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", rotated);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
        using var unaffected = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", otherSession);
        Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
    }

    [Fact]
    public async Task OwnerListsAreBoundedAndTenantScopedOnEveryPage()
    {
        var owner = await RegisterAsync();
        var other = await RegisterAsync();
        var member = await RegisterAsync();
        await AcceptMembershipAsync(owner, member, OrganizationRole.Viewer);
        var tenantId = owner.Tokens.TenantId;
        using var pending = await AuthorizedAsync(HttpMethod.Post, $"/api/v1/organizations/{tenantId}/invitations",
            owner.Tokens, new CreateInvitationRequest("pending@example.test", OrganizationRole.Viewer));
        Assert.Equal(HttpStatusCode.Created, pending.StatusCode);

        foreach (var collection in new[] { "members", "invitations" })
        {
            var path = $"/api/v1/organizations/{tenantId}/{collection}";
            using var first = await AuthorizedAsync(HttpMethod.Get, path + "?offset=0&limit=1", owner.Tokens);
            using var second = await AuthorizedAsync(HttpMethod.Get, path + "?offset=1&limit=1", owner.Tokens);
            using var end = await AuthorizedAsync(HttpMethod.Get, path + "?offset=2&limit=1", owner.Tokens);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal(HttpStatusCode.OK, end.StatusCode);
            var firstItem = Assert.Single((await ReadJsonAsync(first)).AsArray());
            var secondItem = Assert.Single((await ReadJsonAsync(second)).AsArray());
            Assert.NotEqual(firstItem!["id"]!.ToString(), secondItem!["id"]!.ToString());
            Assert.Empty((await ReadJsonAsync(end)).AsArray());
            using var invalid = await AuthorizedAsync(HttpMethod.Get, path + "?offset=-1&limit=101", owner.Tokens);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var foreign = await AuthorizedAsync(HttpMethod.Get, path + "?offset=1&limit=1", other.Tokens);
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }
    }

    [Fact]
    public async Task RequestBodiesCannotSupplyTenantAuthorityOrNumericRoles()
    {
        var owner = await RegisterAsync();
        using var spoof = await AuthorizedAsync(HttpMethod.Patch, $"/api/v1/organizations/{owner.Tokens.TenantId}", owner.Tokens,
            new { name = "Safe name", tenantId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, spoof.StatusCode);
        using var role = await AuthorizedAsync(HttpMethod.Post, $"/api/v1/organizations/{owner.Tokens.TenantId}/invitations", owner.Tokens,
            new { email = "tech@example.test", role = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, role.StatusCode);
    }

    [Fact]
    public async Task ForeignMemberAndInvitationIdsCannotBeUsedUnderAnAuthorizedTenantUrl()
    {
        var first = await RegisterAsync();
        var second = await RegisterAsync();
        var foreignMembership = await MembershipIdAsync(second.Tokens, second.Tokens.UserId);
        using var changed = await AuthorizedAsync(HttpMethod.Patch,
            $"/api/v1/organizations/{first.Tokens.TenantId}/members/{foreignMembership}/role", first.Tokens,
            new ChangeMembershipRoleRequest(OrganizationRole.Viewer));
        Assert.Equal(HttpStatusCode.NotFound, changed.StatusCode);
        using var deactivated = await AuthorizedAsync(HttpMethod.Post,
            $"/api/v1/organizations/{first.Tokens.TenantId}/members/{foreignMembership}/deactivate", first.Tokens);
        Assert.Equal(HttpStatusCode.NotFound, deactivated.StatusCode);
        using var invitation = await AuthorizedAsync(HttpMethod.Post,
            $"/api/v1/organizations/{second.Tokens.TenantId}/invitations", second.Tokens,
            new CreateInvitationRequest("outside@example.test", OrganizationRole.Technician));
        Assert.Equal(HttpStatusCode.Created, invitation.StatusCode);
        var foreignInvitation = (await invitation.Content.ReadFromJsonAsync<OrganizationInvitationDto>(JsonOptions))!.Id;
        using var revoked = await AuthorizedAsync(HttpMethod.Delete,
            $"/api/v1/organizations/{first.Tokens.TenantId}/invitations/{foreignInvitation}", first.Tokens);
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
    }

    [Theory]
    [InlineData(OrganizationRole.Manager)]
    [InlineData(OrganizationRole.Technician)]
    [InlineData(OrganizationRole.Viewer)]
    public async Task NonOwnerRolesCanReadTheirOrganizationButCannotAdministerMembers(OrganizationRole role)
    {
        var owner = await RegisterAsync();
        var member = await RegisterAsync();
        var tokens = await JoinAsync(owner, member, role);
        using var organization = await AuthorizedAsync(HttpMethod.Get, $"/api/v1/organizations/{owner.Tokens.TenantId}", tokens);
        Assert.Equal(HttpStatusCode.OK, organization.StatusCode);
        using var members = await AuthorizedAsync(HttpMethod.Get, $"/api/v1/organizations/{owner.Tokens.TenantId}/members", tokens);
        Assert.Equal(HttpStatusCode.Forbidden, members.StatusCode);
        using var update = await AuthorizedAsync(HttpMethod.Patch, $"/api/v1/organizations/{owner.Tokens.TenantId}", tokens,
            new UpdateOrganizationRequest("Unauthorized", null));
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        using var invite = await AuthorizedAsync(HttpMethod.Post, $"/api/v1/organizations/{owner.Tokens.TenantId}/invitations", tokens,
            new CreateInvitationRequest("unauthorized@example.test", OrganizationRole.OrganizationOwner));
        Assert.Equal(HttpStatusCode.Forbidden, invite.StatusCode);
    }

    [Fact]
    public async Task LastOwnerCannotBeDemotedDeactivatedOrDeactivateTheirAccount()
    {
        var owner = await RegisterAsync();
        var membership = await MembershipIdAsync(owner.Tokens, owner.Tokens.UserId);
        using var demotion = await AuthorizedAsync(HttpMethod.Patch,
            $"/api/v1/organizations/{owner.Tokens.TenantId}/members/{membership}/role", owner.Tokens,
            new ChangeMembershipRoleRequest(OrganizationRole.Manager));
        Assert.Equal(HttpStatusCode.Conflict, demotion.StatusCode);
        using var deactivate = await AuthorizedAsync(HttpMethod.Post,
            $"/api/v1/organizations/{owner.Tokens.TenantId}/members/{membership}/deactivate", owner.Tokens);
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
    }

    [Fact]
    public async Task ConcurrentOwnerDemotionsCannotRemoveEveryOwner()
    {
        var first = await RegisterAsync();
        var second = await RegisterAsync();
        var secondTokens = await JoinAsync(first, second, OrganizationRole.OrganizationOwner);
        var firstMembership = await MembershipIdAsync(first.Tokens, first.Tokens.UserId);
        var secondMembership = await MembershipIdAsync(first.Tokens, second.Tokens.UserId);
        var results = await Task.WhenAll(
            AuthorizedAsync(HttpMethod.Patch, $"/api/v1/organizations/{first.Tokens.TenantId}/members/{firstMembership}/role",
                first.Tokens, new ChangeMembershipRoleRequest(OrganizationRole.Manager)),
            AuthorizedAsync(HttpMethod.Patch, $"/api/v1/organizations/{first.Tokens.TenantId}/members/{secondMembership}/role",
                secondTokens, new ChangeMembershipRoleRequest(OrganizationRole.Manager)));
        try
        {
            Assert.Single(results, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, response => response.StatusCode == HttpStatusCode.Conflict);
            var owners = await InDatabaseAsync(context => context.Set<OrganizationMembership>().IgnoreQueryFilters()
                .CountAsync(member => member.TenantId == first.Tokens.TenantId && member.IsActive && member.Role == OrganizationRole.OrganizationOwner));
            Assert.Equal(1, owners);
        }
        finally
        {
            foreach (var response in results) response.Dispose();
        }
    }

    [Fact]
    public async Task RoleDowngradeTakesEffectWithoutWaitingForOldJwtToExpire()
    {
        var owner = await RegisterAsync();
        var member = await RegisterAsync();
        var memberTokens = await JoinAsync(owner, member, OrganizationRole.OrganizationOwner);
        var membership = await MembershipIdAsync(owner.Tokens, member.Tokens.UserId);
        using var changed = await AuthorizedAsync(HttpMethod.Patch,
            $"/api/v1/organizations/{owner.Tokens.TenantId}/members/{membership}/role", owner.Tokens,
            new ChangeMembershipRoleRequest(OrganizationRole.Viewer));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var forbidden = await AuthorizedAsync(HttpMethod.Get, $"/api/v1/organizations/{owner.Tokens.TenantId}/members", memberTokens);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var me = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", memberTokens);
        Assert.Equal(OrganizationRole.Viewer, (await me.Content.ReadFromJsonAsync<CurrentUserDto>(JsonOptions))!.Role);
    }

    [Fact]
    public async Task DeactivationRevokesSessionsAndReactivationDoesNotResurrectCredentials()
    {
        var owner = await RegisterAsync();
        var member = await RegisterAsync();
        var memberTokens = await JoinAsync(owner, member, OrganizationRole.Technician);
        var membership = await MembershipIdAsync(owner.Tokens, member.Tokens.UserId);
        using var deactivated = await AuthorizedAsync(HttpMethod.Post,
            $"/api/v1/organizations/{owner.Tokens.TenantId}/members/{membership}/deactivate", owner.Tokens);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        using var me = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", memberTokens);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        using var refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { memberTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        using var reactivated = await AuthorizedAsync(HttpMethod.Post,
            $"/api/v1/organizations/{owner.Tokens.TenantId}/members/{membership}/reactivate", owner.Tokens);
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        using var old = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", memberTokens);
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
    }

    [Fact]
    public async Task SelectingVerifiedMembershipRotatesContextAndRevokesPreviousSession()
    {
        var owner = await RegisterAsync();
        var member = await RegisterAsync();
        var selected = await JoinAsync(owner, member, OrganizationRole.Technician);
        Assert.Equal(owner.Tokens.TenantId, selected.TenantId);
        Assert.NotEqual(member.Tokens.SessionId, selected.SessionId);
        var previousExpiry = await InDatabaseAsync(context => context.AuthSessions.Where(session => session.Id == member.Tokens.SessionId)
            .Select(session => session.ExpiresAt).SingleAsync());
        var selectedExpiry = await InDatabaseAsync(context => context.AuthSessions.Where(session => session.Id == selected.SessionId)
            .Select(session => session.ExpiresAt).SingleAsync());
        Assert.Equal(previousExpiry, selectedExpiry);
        using var old = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", member.Tokens);
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        using var current = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", selected);
        Assert.Equal(owner.Tokens.TenantId, (await current.Content.ReadFromJsonAsync<CurrentUserDto>(JsonOptions))!.ActiveOrganizationId);
        using var oldRefresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { member.Tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        using var revoked = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", selected);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task TenantQueryFiltersAndWriteGuardsBlockCrossTenantStateEvenWhenTracked()
    {
        var first = await RegisterAsync();
        var second = await RegisterAsync();
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetValidated(new TenantActor(
            first.Tokens.UserId, first.Tokens.SessionId, first.Tokens.TenantId, first.Tokens.Role));
        var context = scope.ServiceProvider.GetRequiredService<FieldOps.Infrastructure.Persistence.FieldOpsDbContext>();
        var visible = await context.Set<Organization>().ToListAsync();
        Assert.Equal(first.Tokens.TenantId, Assert.Single(visible).Id);
        Assert.Empty(await context.Set<OrganizationMembership>().Where(member => member.TenantId == second.Tokens.TenantId).ToListAsync());

        var foreign = await context.Set<Organization>().IgnoreQueryFilters().SingleAsync(organization => organization.Id == second.Tokens.TenantId);
        foreign.Update("Unauthorized tracked edit", foreign.TimeZoneId, DateTimeOffset.UtcNow);
        var error = await Assert.ThrowsAsync<FieldOpsException>(() => context.SaveChangesAsync());
        Assert.Equal(ErrorKind.Forbidden, error.Kind);
        context.ChangeTracker.Clear();
        context.Set<OrganizationMembership>().Add(OrganizationMembership.Create(second.Tokens.TenantId,
            first.Tokens.UserId, OrganizationRole.OrganizationOwner, DateTimeOffset.UtcNow));
        var insertError = await Assert.ThrowsAsync<FieldOpsException>(() => context.SaveChangesAsync());
        Assert.Equal(ErrorKind.Forbidden, insertError.Kind);
    }

    [Fact]
    public async Task DatabaseCompositeForeignKeyRejectsInvitationIssuedByAnotherTenantMembership()
    {
        var first = await RegisterAsync();
        var second = await RegisterAsync();
        var foreignMembership = await MembershipIdAsync(second.Tokens, second.Tokens.UserId);
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetValidated(new TenantActor(
            first.Tokens.UserId, first.Tokens.SessionId, first.Tokens.TenantId, first.Tokens.Role));
        var context = scope.ServiceProvider.GetRequiredService<FieldOps.Infrastructure.Persistence.FieldOpsDbContext>();
        context.Set<OrganizationInvitation>().Add(OrganizationInvitation.Create(first.Tokens.TenantId, foreignMembership,
            "FOREIGN@EXAMPLE.TEST", OrganizationRole.Viewer, new string('A', 64), DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task DatabaseRejectsASessionForAUserWithoutMembershipInThatTenant()
    {
        var first = await RegisterAsync();
        var second = await RegisterAsync();
        var now = DateTimeOffset.UtcNow;
        await InDatabaseAsync(async context =>
        {
            context.AuthSessions.Add(AuthSession.Create(second.Tokens.UserId, first.Tokens.TenantId,
                "Native", "test-stamp", now, now.AddDays(1)));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        });
    }

    [Fact]
    public async Task MissingRoleCannotDefaultToOwner()
    {
        var owner = await RegisterAsync();
        using var invitation = await AuthorizedAsync(HttpMethod.Post, $"/api/v1/organizations/{owner.Tokens.TenantId}/invitations",
            owner.Tokens, new { email = "missing-role@example.test" });
        Assert.Equal(HttpStatusCode.BadRequest, invitation.StatusCode);
        var membership = await MembershipIdAsync(owner.Tokens, owner.Tokens.UserId);
        using var role = await AuthorizedAsync(HttpMethod.Patch,
            $"/api/v1/organizations/{owner.Tokens.TenantId}/members/{membership}/role", owner.Tokens, new { });
        Assert.Equal(HttpStatusCode.BadRequest, role.StatusCode);
    }
}
