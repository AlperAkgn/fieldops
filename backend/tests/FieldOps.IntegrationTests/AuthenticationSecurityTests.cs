using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FieldOps.Application.Identity;
using FieldOps.Application.Tenancy;
using FieldOps.Domain.Organizations;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FieldOps.IntegrationTests;

[Trait("Category", "Database")]
public sealed class AuthenticationSecurityTests : IdentityTestBase
{
    [Fact]
    public async Task RegistrationCreatesFirstOwnerAndCurrentUser()
    {
        var account = await RegisterAsync();
        Assert.Equal(OrganizationRole.OrganizationOwner, account.Tokens.Role);
        Assert.NotEqual(Guid.Empty, account.Tokens.TenantId);
        Assert.NotEqual(Guid.Empty, account.Tokens.SessionId);
        Assert.True(account.Tokens.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.True(account.Tokens.RefreshExpiresAt > account.Tokens.ExpiresAt);

        using var response = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", account.Tokens);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var current = (await response.Content.ReadFromJsonAsync<CurrentUserDto>(JsonOptions))!;
        Assert.Equal(account.Tokens.UserId, current.Id);
        Assert.Equal(account.Email, current.Email);
        Assert.Equal(account.Tokens.TenantId, current.ActiveOrganizationId);
        Assert.Equal(OrganizationRole.OrganizationOwner, current.Role);
        Assert.True(current.EmailConfirmed);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);

        var user = await InDatabaseAsync(context => context.Users.SingleAsync(user => user.Id == account.Tokens.UserId));
        Assert.NotNull(user.PasswordHash);
        Assert.NotEqual(account.Password, user.PasswordHash);
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account.Tokens.RefreshToken)));
        var refresh = await InDatabaseAsync(context => context.RefreshCredentials.SingleAsync(token => token.SessionId == account.Tokens.SessionId));
        Assert.Equal(expectedHash, refresh.TokenHash);
        Assert.NotEqual(account.Tokens.RefreshToken, refresh.TokenHash);
        Assert.Equal(1, await InDatabaseAsync(context => context.OrganizationMemberships.IgnoreQueryFilters()
            .CountAsync(member => member.UserId == account.Tokens.UserId && member.TenantId == account.Tokens.TenantId
                && member.Role == OrganizationRole.OrganizationOwner && member.IsActive)));
    }

    [Fact]
    public async Task WrongPasswordAndUnknownAccountReturnTheSamePublicError()
    {
        var account = await RegisterAsync();
        using var wrongPassword = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(account.Email, "Incorrect-Password-42!"));
        using var unknownUser = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("absent@example.test", "Incorrect-Password-42!"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(wrongPassword.StatusCode, unknownUser.StatusCode);
        var first = await ReadJsonAsync(wrongPassword);
        var second = await ReadJsonAsync(unknownUser);
        Assert.Equal(first["code"]?.ToString(), second["code"]?.ToString());
        Assert.Equal(first["detail"]?.ToString(), second["detail"]?.ToString());
    }

    [Theory]
    [InlineData("not-an-email", "Test User")]
    [InlineData("first'last@example.test", "Test User")]
    [InlineData("valid@example.test", "Test\0User")]
    [InlineData("valid@example.test", "Test\u0085User")]
    [InlineData("valid@example.test", "Test User\t")]
    [InlineData("valid@example.test", " ")]
    public async Task InvalidRegistrationIsRejectedWithoutAnAccount(string email, string displayName)
    {
        using var response = await Client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, displayName, "Test Organization", "UTC"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await InDatabaseAsync(context => context.Users.CountAsync()));
        Assert.Equal(0, await InDatabaseAsync(context => context.OutboundEmails.CountAsync()));
        Assert.Equal(0, await InDatabaseAsync(context => context.Organizations.IgnoreQueryFilters().CountAsync()));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("tenant")]
    [InlineData("session")]
    public async Task InvalidSignedOrTamperedAccessTokenCannotAuthenticate(string mutation)
    {
        var account = await RegisterAsync();
        var token = ForgeToken(account.Tokens.AccessToken, mutation);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(ApiFactory.TestSigningKey, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ThrottledSignedRequestDoesNotReachSessionValidation()
    {
        var account = await RegisterAsync();
        var actor = new TenantActor(account.Tokens.UserId, account.Tokens.SessionId, account.Tokens.TenantId, account.Tokens.Role);
        var validator = new CountingSessionValidator(actor);
        await using var factory = new ApiFactory(connectionString: DatabaseConnectionString,
            settings: new Dictionary<string, string?> { ["RateLimits:IpPermitLimit"] = "1" })
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISessionValidator>();
                services.AddSingleton<ISessionValidator>(validator);
            }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.Tokens.AccessToken);
        using var first = await client.GetAsync("/api/v1/organizations");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1, validator.Calls);
        using var rejected = await client.GetAsync("/api/v1/organizations");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("rate_limit_exceeded", (await ReadJsonAsync(rejected))["code"]!.GetValue<string>());
        Assert.Equal(1, validator.Calls);
    }

    private sealed class CountingSessionValidator(TenantActor actor) : ISessionValidator
    {
        public int Calls { get; private set; }

        public Task<TenantActor?> ValidateSessionAsync(Guid userId, Guid sessionId, Guid tenantId, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal((actor.UserId, actor.SessionId, actor.TenantId), (userId, sessionId, tenantId));
            return Task.FromResult<TenantActor?>(actor);
        }
    }

    [Fact]
    public async Task RefreshRotatesAndReuseRevokesTheEntireSessionFamily()
    {
        var account = await RegisterAsync();
        using var refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { account.Tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = (await refresh.Content.ReadFromJsonAsync<TokenPair>(JsonOptions))!;
        Assert.NotEqual(account.Tokens.RefreshToken, rotated.RefreshToken);
        Assert.Equal(account.Tokens.SessionId, rotated.SessionId);

        using var replay = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { account.Tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var access = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", rotated);
        Assert.Equal(HttpStatusCode.Unauthorized, access.StatusCode);
        using var laterRefresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, laterRefresh.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRefreshHasOneWinnerAndDetectedReuseRevokesTheFamily()
    {
        var account = await RegisterAsync();
        var responses = await Task.WhenAll(
            Client.PostAsJsonAsync("/api/v1/auth/refresh", new { account.Tokens.RefreshToken }),
            Client.PostAsJsonAsync("/api/v1/auth/refresh", new { account.Tokens.RefreshToken }));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
            var winner = (await responses.Single(response => response.StatusCode == HttpStatusCode.OK)
                .Content.ReadFromJsonAsync<TokenPair>(JsonOptions))!;
            using var current = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", winner);
            Assert.Equal(HttpStatusCode.Unauthorized, current.StatusCode);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task ReplayOfNativeRefreshRevokesSwitchedDescendantsOnly()
    {
        var owner = await RegisterAsync();
        var member = await RegisterAsync();
        await AcceptMembershipAsync(owner, member, OrganizationRole.Technician);
        var independent = await LoginAsync(member);
        using var refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { member.Tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = (await refresh.Content.ReadFromJsonAsync<TokenPair>(JsonOptions))!;
        using var selection = await AuthorizedAsync(HttpMethod.Post, $"/api/v1/organizations/{owner.Tokens.TenantId}/select",
            rotated, new { rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, selection.StatusCode);
        var successor = (await selection.Content.ReadFromJsonAsync<TokenPair>(JsonOptions))!;
        Assert.Equal(OrganizationRole.Technician, successor.Role);
        using var roleDenied = await AuthorizedAsync(HttpMethod.Get, $"/api/v1/organizations/{owner.Tokens.TenantId}/members", successor);
        Assert.Equal(HttpStatusCode.Forbidden, roleDenied.StatusCode);
        using var replay = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { member.Tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var access = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", successor);
        Assert.Equal(HttpStatusCode.Unauthorized, access.StatusCode);
        using var laterRefresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { successor.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, laterRefresh.StatusCode);
        using var unaffected = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", independent);
        Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRefreshAndSelectionNeverLeaveTheirWinningCredentialActive()
    {
        var account = await RegisterAsync();
        var tokens = account.Tokens;
        var payload = new { tokens.RefreshToken };
        var responses = await Task.WhenAll(
            Client.PostAsJsonAsync("/api/v1/auth/refresh", payload),
            AuthorizedAsync(HttpMethod.Post, $"/api/v1/organizations/{tokens.TenantId}/select", tokens, payload));
        try
        {
            var winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
            var pair = (await winner.Content.ReadFromJsonAsync<TokenPair>(JsonOptions))!;
            using var current = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", pair);
            Assert.Equal(HttpStatusCode.Unauthorized, current.StatusCode);
            using var refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { pair.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task ConcurrentNativeSelectionsRevokeTheWinnerButPreserveIndependentLogin()
    {
        var owner = await RegisterAsync();
        var member = await RegisterAsync();
        await AcceptMembershipAsync(owner, member, OrganizationRole.Technician);
        var independent = await LoginAsync(member);
        var path = $"/api/v1/organizations/{owner.Tokens.TenantId}/select";
        var payload = new { member.Tokens.RefreshToken };
        var responses = await Task.WhenAll(
            AuthorizedAsync(HttpMethod.Post, path, member.Tokens, payload),
            AuthorizedAsync(HttpMethod.Post, path, member.Tokens, payload));
        try
        {
            var winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
            var pair = (await winner.Content.ReadFromJsonAsync<TokenPair>(JsonOptions))!;
            Assert.Equal(owner.Tokens.TenantId, pair.TenantId);
            Assert.Equal(OrganizationRole.Technician, pair.Role);
            await AssertNativeSessionRevokedAsync(pair);
            using var unaffected = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", independent);
            Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
            using var independentRefresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { independent.RefreshToken });
            Assert.Equal(HttpStatusCode.OK, independentRefresh.StatusCode);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task NativeSelectionReplayRequiresTheSignedSessionAndItsExactConsumedCredentialAcrossTwoSwitches()
    {
        var owner = await RegisterAsync();
        var member = await RegisterAsync();
        await AcceptMembershipAsync(owner, member, OrganizationRole.Technician);
        var independent = await LoginAsync(member);
        var original = member.Tokens;
        var target = $"/api/v1/organizations/{owner.Tokens.TenantId}/select";
        using var first = await AuthorizedAsync(HttpMethod.Post, target, original, new { original.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var switched = (await first.Content.ReadFromJsonAsync<TokenPair>(JsonOptions))!;
        using var second = await AuthorizedAsync(HttpMethod.Post, $"/api/v1/organizations/{original.TenantId}/select",
            switched, new { switched.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var returned = (await second.Content.ReadFromJsonAsync<TokenPair>(JsonOptions))!;
        using var refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { returned.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var successor = (await refresh.Content.ReadFromJsonAsync<TokenPair>(JsonOptions))!;
        Assert.Equal(original.TenantId, successor.TenantId);

        var sessions = await InDatabaseAsync(database => database.AuthSessions.AsNoTracking()
            .Where(session => session.FamilyId == original.SessionId).ToListAsync());
        Assert.Equal(3, sessions.Count);
        Assert.Single(sessions.Select(session => session.ExpiresAt).Distinct());

        foreach (var mutation in new[] { "signature", "expired", "tenant", "session" })
        {
            var tampered = original with { AccessToken = ForgeToken(original.AccessToken, mutation) };
            using var invalid = await AuthorizedAsync(HttpMethod.Post, target, tampered, new { original.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        }

        using var random = await AuthorizedAsync(HttpMethod.Post, target, original, new { refreshToken = Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.Unauthorized, random.StatusCode);
        using var anotherFamily = await AuthorizedAsync(HttpMethod.Post, target, independent, new { original.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, anotherFamily.StatusCode);
        using var wrongSessionInFamily = await AuthorizedAsync(HttpMethod.Post, target, successor, new { original.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongSessionInFamily.StatusCode);
        using var stillActive = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", successor);
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);

        using var replay = await AuthorizedAsync(HttpMethod.Post, target, original, new { original.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        await AssertNativeSessionRevokedAsync(successor);
        using var unaffected = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", independent);
        Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
    }

    private async Task AssertNativeSessionRevokedAsync(TokenPair pair)
    {
        using var current = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", pair);
        Assert.Equal(HttpStatusCode.Unauthorized, current.StatusCode);
        using var refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { pair.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task LogoutImmediatelyRevokesAccessAndRefreshCredentials()
    {
        var account = await RegisterAsync();
        using var logout = await AuthorizedAsync(HttpMethod.Post, "/api/v1/auth/logout", account.Tokens);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var current = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", account.Tokens);
        Assert.Equal(HttpStatusCode.Unauthorized, current.StatusCode);
        using var refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { account.Tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task UserCanListAndRevokeOwnSessionsButCannotRevokeAnotherUsersSession()
    {
        var account = await RegisterAsync();
        var secondSession = await LoginAsync(account);
        var outsider = await RegisterAsync();
        using var sessions = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/sessions", secondSession);
        Assert.Equal(HttpStatusCode.OK, sessions.StatusCode);
        var listed = (await sessions.Content.ReadFromJsonAsync<SessionDto[]>(JsonOptions))!;
        Assert.Equal(2, listed.Length);
        Assert.Single(listed, session => session.IsCurrent && session.Id == secondSession.SessionId);
        using var forbidden = await AuthorizedAsync(HttpMethod.Delete, $"/api/v1/auth/sessions/{outsider.Tokens.SessionId}", secondSession);
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);

        using var revoked = await AuthorizedAsync(HttpMethod.Delete, $"/api/v1/auth/sessions/{account.Tokens.SessionId}", secondSession);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var old = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", account.Tokens);
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        using var current = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", secondSession);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        using var all = await AuthorizedAsync(HttpMethod.Post, "/api/v1/auth/logout-all", secondSession);
        Assert.Equal(HttpStatusCode.NoContent, all.StatusCode);
        using var afterAll = await AuthorizedAsync(HttpMethod.Get, "/api/v1/auth/me", secondSession);
        Assert.Equal(HttpStatusCode.Unauthorized, afterAll.StatusCode);
    }

    [Fact]
    public async Task NativeTransportRejectsBrowserOriginAndFetchMetadata()
    {
        var account = await RegisterAsync();
        foreach (var header in new[] { "Origin", "Sec-Fetch-Site" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest(account.Email, account.Password))
            };
            request.Headers.Add(header, header == "Origin" ? "http://localhost:5173" : "same-origin");
            using var response = await Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }
    }

    [Fact]
    public async Task ConcurrentRegistrationForNormalizedEmailCreatesOnlyOneCompleteOrganization()
    {
        var request = new RegisterRequest("unique@example.test", "Çağrı 👩‍💻", "Test Organization");
        var responses = await Task.WhenAll(
            Client.PostAsJsonAsync("/api/v1/auth/register", request, JsonOptions),
            Client.PostAsJsonAsync("/api/v1/auth/register", request with { Email = "UNIQUE@EXAMPLE.TEST" }, JsonOptions));
        try
        {
            Assert.All(responses, response =>
            {
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
                Assert.Null(response.Headers.Location);
                Assert.False(response.Headers.Contains("Set-Cookie"));
            });
            Assert.Equal(await responses[0].Content.ReadAsStringAsync(), await responses[1].Content.ReadAsStringAsync());
            var user = await InDatabaseAsync(context => context.Users.SingleAsync());
            Assert.Equal(request.DisplayName, user.DisplayName);
            Assert.False(user.EmailConfirmed);
            Assert.Null(user.PasswordHash);
            Assert.Equal(1, await InDatabaseAsync(context => context.Organizations.IgnoreQueryFilters().CountAsync()));
            Assert.Equal(1, await InDatabaseAsync(context => context.OrganizationMemberships.IgnoreQueryFilters().CountAsync()));
            Assert.Equal(0, await InDatabaseAsync(context => context.AuthSessions.CountAsync()));
            Assert.Equal(1, await InDatabaseAsync(context => context.OutboundEmails.CountAsync()));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    private static string ForgeToken(string original, string mutation)
    {
        var parts = original.Split('.');
        var payload = JsonNode.Parse(Encoding.UTF8.GetString(DecodeBase64(parts[1])))!;
        switch (mutation)
        {
            case "expired": payload["exp"] = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds(); break;
            case "issuer": payload["iss"] = "Untrusted.Issuer"; break;
            case "audience": payload["aud"] = "Untrusted.Audience"; break;
            case "tenant": payload["tenant_id"] = Guid.NewGuid().ToString(); break;
            case "session": payload["sid"] = Guid.NewGuid().ToString(); break;
        }

        var content = parts[0] + "." + EncodeBase64(Encoding.UTF8.GetBytes(payload.ToJsonString()));
        var key = mutation == "signature" ? new string('x', 80) : ApiFactory.TestSigningKey;
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.ASCII.GetBytes(content));
        return content + "." + EncodeBase64(signature);
    }

    private static string EncodeBase64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] DecodeBase64(string value)
        => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
}
