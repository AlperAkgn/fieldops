using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FieldOps.Application.Organizations;
using FieldOps.Application.Tenancy;
using FieldOps.Domain.Organizations;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.IntegrationTests;

[Trait("Category", "Database")]
public sealed class InvitationSecurityTests : IdentityTestBase
{
    [Fact]
    public async Task InvitationRequiresTheIntendedVerifiedEmailAndCanBeAcceptedOnlyOnce()
    {
        var owner = await RegisterAsync();
        var recipient = await RegisterAsync();
        var wrongAccount = await RegisterAsync();
        var invitation = await InviteAsync(owner, recipient.Email, OrganizationRole.Technician);

        // Model an unconfirmed legacy account with an already-issued session.
        await InDatabaseAsync(async context =>
        {
            var user = await context.Users.SingleAsync(user => user.Id == recipient.Tokens.UserId);
            user.EmailConfirmed = false;
            await context.SaveChangesAsync();
        });
        using var unverified = await AuthorizedAsync(HttpMethod.Post, "/api/v1/invitations/accept", recipient.Tokens,
            new AcceptInvitationRequest(invitation.Token));
        Assert.Equal(HttpStatusCode.Unauthorized, unverified.StatusCode);
        using var wrongEmail = await AuthorizedAsync(HttpMethod.Post, "/api/v1/invitations/accept", wrongAccount.Tokens,
            new AcceptInvitationRequest(invitation.Token));
        Assert.Equal(HttpStatusCode.BadRequest, wrongEmail.StatusCode);
        using var requested = await Client.PostAsJsonAsync("/api/v1/auth/request-email-confirmation", new { recipient.Email });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        await ConfirmRegistrationAsync(new PendingAccount(recipient.Email, recipient.Password));
        var confirmed = await LoginAsync(recipient);
        using var accepted = await AuthorizedAsync(HttpMethod.Post, "/api/v1/invitations/accept", confirmed,
            new AcceptInvitationRequest(invitation.Token));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var membership = await accepted.Content.ReadFromJsonAsync<OrganizationSummary>(JsonOptions);
        Assert.Equal(owner.Tokens.TenantId, membership!.Id);
        Assert.Equal(OrganizationRole.Technician, membership.Role);
        using var replay = await AuthorizedAsync(HttpMethod.Post, "/api/v1/invitations/accept", confirmed,
            new AcceptInvitationRequest(invitation.Token));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task InvitationResponseAndStorageNeverExposeRawToken()
    {
        var owner = await RegisterAsync();
        var invitation = await InviteAsync(owner, "new-technician@example.test", OrganizationRole.Technician);
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(invitation.Token)));
        var stored = await InDatabaseAsync(context => context.OrganizationInvitations.IgnoreQueryFilters()
            .SingleAsync(item => item.Id == invitation.Id));
        Assert.Equal(expectedHash, stored.TokenHash);
        Assert.NotEqual(invitation.Token, stored.TokenHash);

        using var listed = await AuthorizedAsync(HttpMethod.Get, $"/api/v1/organizations/{owner.Tokens.TenantId}/invitations", owner.Tokens);
        var json = await listed.Content.ReadAsStringAsync();
        Assert.DoesNotContain(invitation.Token, json);
        Assert.DoesNotContain(expectedHash, json);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(invitation.ProtectedBody);
        Assert.DoesNotContain(invitation.Token, invitation.ProtectedBody);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredOrRevokedInvitationCannotBeAccepted(bool revoke)
    {
        var owner = await RegisterAsync();
        var recipient = await RegisterAsync();
        var invitation = await InviteAsync(owner, recipient.Email, OrganizationRole.Manager);
        if (revoke)
        {
            using var response = await AuthorizedAsync(HttpMethod.Delete,
                $"/api/v1/organizations/{owner.Tokens.TenantId}/invitations/{invitation.Id}", owner.Tokens);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        else
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetValidated(new TenantActor(
                owner.Tokens.UserId, owner.Tokens.SessionId, owner.Tokens.TenantId, owner.Tokens.Role));
            var database = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
            var entity = await database.OrganizationInvitations.SingleAsync(item => item.Id == invitation.Id);
            database.Entry(entity).Property(item => item.CreatedAt).CurrentValue = DateTimeOffset.UtcNow.AddHours(-2);
            database.Entry(entity).Property(item => item.ExpiresAt).CurrentValue = DateTimeOffset.UtcNow.AddHours(-1);
            await database.SaveChangesAsync();
        }

        using var accepted = await AuthorizedAsync(HttpMethod.Post, "/api/v1/invitations/accept", recipient.Tokens,
            new AcceptInvitationRequest(invitation.Token));
        Assert.Equal(HttpStatusCode.BadRequest, accepted.StatusCode);
        Assert.False(await InDatabaseAsync(context => context.OrganizationMemberships.IgnoreQueryFilters()
            .AnyAsync(member => member.TenantId == owner.Tokens.TenantId && member.UserId == recipient.Tokens.UserId)));
    }

    [Fact]
    public async Task ConcurrentAcceptanceCreatesExactlyOneMembership()
    {
        var owner = await RegisterAsync();
        var recipient = await RegisterAsync();
        var invitation = await InviteAsync(owner, recipient.Email, OrganizationRole.Viewer);
        var responses = await Task.WhenAll(
            AuthorizedAsync(HttpMethod.Post, "/api/v1/invitations/accept", recipient.Tokens, new AcceptInvitationRequest(invitation.Token)),
            AuthorizedAsync(HttpMethod.Post, "/api/v1/invitations/accept", recipient.Tokens, new AcceptInvitationRequest(invitation.Token)));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
            Assert.Equal(1, await InDatabaseAsync(context => context.OrganizationMemberships.IgnoreQueryFilters()
                .CountAsync(member => member.TenantId == owner.Tokens.TenantId && member.UserId == recipient.Tokens.UserId)));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task PendingInvitationCannotGrantAccessAfterIssuerLosesOwnerRole()
    {
        var owner = await RegisterAsync();
        var successor = await RegisterAsync();
        var successorTokens = await JoinAsync(owner, successor, OrganizationRole.OrganizationOwner);
        var recipient = await RegisterAsync();
        var invitation = await InviteAsync(owner, recipient.Email, OrganizationRole.Technician);
        var issuer = await MembershipIdAsync(successorTokens, owner.Tokens.UserId);
        using var changed = await AuthorizedAsync(HttpMethod.Patch,
            $"/api/v1/organizations/{owner.Tokens.TenantId}/members/{issuer}/role", successorTokens,
            new ChangeMembershipRoleRequest(OrganizationRole.Manager));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var accepted = await AuthorizedAsync(HttpMethod.Post, "/api/v1/invitations/accept", recipient.Tokens,
            new AcceptInvitationRequest(invitation.Token));
        Assert.Equal(HttpStatusCode.BadRequest, accepted.StatusCode);
        Assert.False(await InDatabaseAsync(context => context.OrganizationMemberships.IgnoreQueryFilters()
            .AnyAsync(member => member.TenantId == owner.Tokens.TenantId && member.UserId == recipient.Tokens.UserId)));
    }

    [Fact]
    public async Task ReplacingExpiredInvitationRevokesOldCredentialAndCreatesUsableReplacement()
    {
        var owner = await RegisterAsync();
        var recipient = await RegisterAsync();
        var first = await InviteAsync(owner, recipient.Email, OrganizationRole.Manager);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetValidated(new TenantActor(
                owner.Tokens.UserId, owner.Tokens.SessionId, owner.Tokens.TenantId, owner.Tokens.Role));
            var database = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
            var invitation = await database.OrganizationInvitations.SingleAsync(item => item.Id == first.Id);
            database.Entry(invitation).Property(item => item.CreatedAt).CurrentValue = DateTimeOffset.UtcNow.AddHours(-2);
            database.Entry(invitation).Property(item => item.ExpiresAt).CurrentValue = DateTimeOffset.UtcNow.AddHours(-1);
            await database.SaveChangesAsync();
        }

        var replacement = await InviteAsync(owner, recipient.Email, OrganizationRole.Viewer);
        Assert.NotEqual(first.Id, replacement.Id);
        Assert.NotEqual(first.Token, replacement.Token);
        var revoked = await InDatabaseAsync(context => context.OrganizationInvitations.IgnoreQueryFilters()
            .Where(item => item.Id == first.Id).Select(item => item.RevokedAt).SingleAsync());
        Assert.NotNull(revoked);
        using var old = await AuthorizedAsync(HttpMethod.Post, "/api/v1/invitations/accept", recipient.Tokens,
            new AcceptInvitationRequest(first.Token));
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        using var accepted = await AuthorizedAsync(HttpMethod.Post, "/api/v1/invitations/accept", recipient.Tokens,
            new AcceptInvitationRequest(replacement.Token));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(OrganizationRole.Viewer, (await accepted.Content.ReadFromJsonAsync<OrganizationSummary>(JsonOptions))!.Role);
    }

    private async Task<(Guid Id, string Token, string ProtectedBody)> InviteAsync(TestAccount owner, string recipient, OrganizationRole role)
    {
        using var response = await AuthorizedAsync(HttpMethod.Post,
            $"/api/v1/organizations/{owner.Tokens.TenantId}/invitations", owner.Tokens, new CreateInvitationRequest(recipient, role));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<OrganizationInvitationDto>(JsonOptions))!;
        var protectedBody = await InDatabaseAsync(context => context.OutboundEmails
            .Where(email => email.ToEmail == recipient && email.Subject.Contains("invitation"))
            .Select(email => email.ProtectedBody).SingleAsync());
        var email = await DeliveredEmailAsync(recipient, "invitation");
        var token = MessageValue(email, "Token:");
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync());
        return (dto.Id, token, protectedBody);
    }
}
