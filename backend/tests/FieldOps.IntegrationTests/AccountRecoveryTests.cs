using System.Net;
using System.Net.Http.Json;
using System.Data;
using FieldOps.Application.Identity;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.IntegrationTests;

[Trait("Category", "Database")]
public sealed class AccountRecoveryTests : IdentityTestBase
{
    [Fact]
    public async Task PasswordRecoveryIsNonEnumeratingAndResetRevokesEverySession()
    {
        var account = await RegisterAsync();
        var otherSession = await LoginAsync(account);
        using var known = await Client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { account.Email });
        using var unknown = await Client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "absent@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.DoesNotContain("token", await known.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var email = await DeliveredEmailAsync(account.Email, "reset");
        Assert.DoesNotContain(Emails.Messages, message => message.ToEmail == "absent@example.test");
        Assert.False(await InDatabaseAsync(context => context.OutboundEmails.AnyAsync(message => message.ToEmail == "absent@example.test")));
        var token = MessageValue(email, "Token:");
        using var invalid = await Client.PostAsJsonAsync("/api/v1/auth/reset-password", new
        {
            userId = account.Tokens.UserId,
            token = "invalid-reset-token",
            newPassword = "New-Valid-Test-Password-43!"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var reset = await Client.PostAsJsonAsync("/api/v1/auth/reset-password", new
        {
            userId = account.Tokens.UserId,
            token,
            newPassword = "New-Valid-Test-Password-43!"
        });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        foreach (var session in new[] { account.Tokens, otherSession })
        {
            using var oldAccess = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", session);
            Assert.Equal(HttpStatusCode.Unauthorized, oldAccess.StatusCode);
            using var refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { session.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }

        using var oldPassword = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(account.Email, account.Password));
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        using var newPassword = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(account.Email, "New-Valid-Test-Password-43!"));
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
        using var replay = await Client.PostAsJsonAsync("/api/v1/auth/reset-password", new
        {
            userId = account.Tokens.UserId,
            token,
            newPassword = "Another-Valid-Test-Password-44!"
        });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task EmailConfirmationIsAccountBoundAndChangesCurrentUserState()
    {
        var account = await BeginRegistrationAsync();
        var other = await RegisterAsync();
        using var requested = await Client.PostAsJsonAsync("/api/v1/auth/request-email-confirmation", new { account.Email });
        using var unknown = await Client.PostAsJsonAsync("/api/v1/auth/request-email-confirmation", new { email = "absent@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        Assert.Equal(requested.StatusCode, unknown.StatusCode);
        Assert.Equal(await requested.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        var email = await DeliveredEmailAsync(account.Email, "confirm");
        Assert.DoesNotContain(Emails.Messages, message => message.ToEmail == "absent@example.test");
        Assert.False(await InDatabaseAsync(context => context.OutboundEmails.AnyAsync(message => message.ToEmail == "absent@example.test")));
        var token = MessageValue(email, "Token:");
        var userId = Guid.Parse(MessageValue(email, "User ID:"));
        using var wrongAccount = await Client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId = other.Tokens.UserId, token, account.Password });
        Assert.Equal(HttpStatusCode.BadRequest, wrongAccount.StatusCode);
        using var earlyLogin = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(account.Email, account.Password));
        Assert.Equal(HttpStatusCode.Unauthorized, earlyLogin.StatusCode);
        using var weakPassword = await Client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId, token, password = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, weakPassword.StatusCode);
        using var invalidPolicy = await Client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId, token, password = "alllowercasecharacters" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPolicy.StatusCode);
        Assert.False(await InDatabaseAsync(context => context.Users.Where(user => user.Id == userId).Select(user => user.EmailConfirmed).SingleAsync()));
        using var confirmed = await Client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId, token, account.Password });
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        using var replay = await Client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId, token, password = "Different-Password-43!" });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        using var login = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(account.Email, account.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var pair = (await login.Content.ReadFromJsonAsync<TokenPair>(JsonOptions))!;
        using var current = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", pair);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.True((await current.Content.ReadFromJsonAsync<CurrentUserDto>(JsonOptions))!.EmailConfirmed);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task LiveSessionsCannotBypassIncompleteAccountActivation(bool emailConfirmed, bool hasPassword)
    {
        var account = await RegisterAsync();
        var selectionSession = await LoginAsync(account);
        var refreshSession = await LoginAsync(account);
        var changed = await InDatabaseAsync(database => database.Users.Where(user => user.Id == account.Tokens.UserId)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.EmailConfirmed, emailConfirmed)
                .SetProperty(user => user.PasswordHash, user => hasPassword ? user.PasswordHash : null)));
        Assert.Equal(1, changed);

        using var access = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", account.Tokens);
        using var selection = await AuthorizedAsync(HttpMethod.Post, $"/api/v1/organizations/{selectionSession.TenantId}/select",
            selectionSession, new { selectionSession.RefreshToken });
        using var refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshSession.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, access.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, selection.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task SoleOwnerCannotDeactivateTheirAccount()
    {
        var account = await RegisterAsync();
        using var deactivate = await AuthorizedAsync(HttpMethod.Post, "/api/v1/auth/deactivate", account.Tokens);
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
        using var current = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", account.Tokens);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    [Fact]
    public async Task AccountDeactivationAfterOwnershipTransferRevokesAllSessionsAndBlocksLogin()
    {
        var account = await RegisterAsync();
        var successor = await RegisterAsync();
        _ = await JoinAsync(account, successor, OrganizationRole.OrganizationOwner);
        var secondSession = await LoginAsync(account);
        using var deactivated = await AuthorizedAsync(HttpMethod.Post, "/api/v1/auth/deactivate", account.Tokens);
        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
        foreach (var tokens in new[] { account.Tokens, secondSession })
        {
            using var current = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", tokens);
            Assert.Equal(HttpStatusCode.Unauthorized, current.StatusCode);
            using var refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { tokens.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }

        using var login = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(account.Email, account.Password));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task AccountDeactivationRechecksOwnerRoleAfterWaitingForOrganizationLock()
    {
        var target = await RegisterAsync();
        var other = await RegisterAsync();
        var otherTokens = await JoinAsync(target, other, OrganizationRole.OrganizationOwner);
        var targetMembership = await MembershipIdAsync(otherTokens, target.Tokens.UserId);
        var otherMembership = await MembershipIdAsync(otherTokens, other.Tokens.UserId);
        using var demoted = await AuthorizedAsync(HttpMethod.Patch,
            $"/api/v1/organizations/{target.Tokens.TenantId}/members/{targetMembership}/role", otherTokens,
            new FieldOps.Application.Organizations.ChangeMembershipRoleRequest(OrganizationRole.Viewer));
        Assert.Equal(HttpStatusCode.OK, demoted.StatusCode);

        await using var connection = new NpgsqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await using (var acquire = new NpgsqlCommand("SELECT \"Id\" FROM \"Organizations\" WHERE \"Id\" = @tenant FOR UPDATE", connection, transaction))
        {
            acquire.Parameters.AddWithValue("tenant", target.Tokens.TenantId);
            await acquire.ExecuteScalarAsync();
        }

        var deactivate = AuthorizedAsync(HttpMethod.Post, "/api/v1/auth/deactivate", target.Tokens);
        await WaitForOrganizationLockAsync(deactivate);
        // Simulate another authorized owner's committed role transfer while the
        // deactivation request is waiting. Role updates do not lock the user row.
        await using (var roles = new NpgsqlCommand("""
            UPDATE "OrganizationMemberships" SET "Role" = 'OrganizationOwner' WHERE "Id" = @target;
            UPDATE "OrganizationMemberships" SET "Role" = 'Viewer' WHERE "Id" = @other;
            """, connection, transaction))
        {
            roles.Parameters.AddWithValue("target", targetMembership);
            roles.Parameters.AddWithValue("other", otherMembership);
            await roles.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        using var response = await deactivate;
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(await InDatabaseAsync(context => context.Users.Where(user => user.Id == target.Tokens.UserId)
            .Select(user => user.IsActive).SingleAsync()));
        Assert.Equal(1, await InDatabaseAsync(context => context.OrganizationMemberships.IgnoreQueryFilters()
            .CountAsync(member => member.TenantId == target.Tokens.TenantId && member.IsActive
                && member.Role == OrganizationRole.OrganizationOwner)));
    }

    private async Task WaitForOrganizationLockAsync(Task<HttpResponseMessage> pending)
    {
        await using var observer = new NpgsqlConnection(DatabaseConnectionString);
        await observer.OpenAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!deadline.IsCancellationRequested)
        {
            Assert.False(pending.IsCompleted, "The deactivation request must wait on the held organization row lock.");
            await using var command = new NpgsqlCommand("""
                SELECT count(*) FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock'
                    AND query LIKE '%Organizations%' AND pid <> pg_backend_pid()
                """, observer);
            if (Convert.ToInt64(await command.ExecuteScalarAsync(deadline.Token)) > 0) return;
            await Task.Delay(25, deadline.Token);
        }

        throw new Xunit.Sdk.XunitException("The deactivation request did not reach the organization lock.");
    }
}
