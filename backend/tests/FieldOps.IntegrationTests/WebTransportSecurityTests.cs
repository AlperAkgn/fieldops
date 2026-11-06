using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FieldOps.Application.Identity;
using FieldOps.Domain.Organizations;

namespace FieldOps.IntegrationTests;

[Trait("Category", "Database")]
public sealed class WebTransportSecurityTests : IdentityTestBase
{
    private const string Origin = "https://localhost:5173";
    private const string RefreshCookie = "__Secure-fieldops-refresh";

    [Fact]
    public async Task HttpCsrfRequestReturnsSafeBadRequestWithoutCookies()
    {
        using var response = await Client.GetAsync("http://localhost/api/v1/auth/web/csrf");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("https_required", (await ReadJsonAsync(response))["code"]!.GetValue<string>());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task WebRegistrationReturnsTheSameAcceptedResponseWithoutIssuingCookies()
    {
        var csrf = await CsrfAsync();
        var request = new RegisterRequest("web-registrant@example.test", "Web User", "Web Organization");
        using var created = await WebRequestAsync("/api/v1/auth/web/register", csrf, request);
        using var duplicate = await WebRequestAsync("/api/v1/auth/web/register", csrf, request with { Email = request.Email.ToUpperInvariant() });
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        Assert.Equal(created.StatusCode, duplicate.StatusCode);
        Assert.Equal(await created.Content.ReadAsStringAsync(), await duplicate.Content.ReadAsStringAsync());
        Assert.False(created.Headers.Contains("Set-Cookie"));
        Assert.False(duplicate.Headers.Contains("Set-Cookie"));
        Assert.Null(created.Headers.Location);
        Assert.Null(duplicate.Headers.Location);
        await ConfirmRegistrationAsync(new PendingAccount(request.Email, "Valid-Test-Password-42!"));
    }

    [Fact]
    public async Task WebLoginUsesProtectedCookieAndNeverReturnsRefreshCredentialInJson()
    {
        var account = await RegisterAsync();
        var csrf = await CsrfAsync();
        using var login = await WebRequestAsync("/api/v1/auth/web/login", csrf,
            new LoginRequest(account.Email, account.Password));

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await ReadJsonAsync(login);
        Assert.NotNull(body["accessToken"]);
        Assert.False(body.AsObject().ContainsKey("refreshToken"));
        var setCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith(RefreshCookie + "=", StringComparison.Ordinal));
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth/web", setCookie, StringComparison.OrdinalIgnoreCase);

        var webCredential = CookieValue(setCookie);
        using var wrongTransport = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = webCredential });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongTransport.StatusCode);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("https://untrusted.example", true)]
    [InlineData("http://localhost:5173", true)]
    [InlineData(Origin, false)]
    public async Task WebLoginRejectsMissingOrUntrustedOriginAndMissingAntiforgery(string? origin, bool includeToken)
    {
        var account = await RegisterAsync();
        var csrf = await CsrfAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/web/login")
        {
            Content = JsonContent.Create(new LoginRequest(account.Email, account.Password))
        };
        if (origin is not null) request.Headers.Add("Origin", origin);
        request.Headers.Add("Cookie", csrf.Cookie);
        if (includeToken) request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            value => value.StartsWith(RefreshCookie + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WebRefreshRequiresWebCredentialAndRotatesCookie()
    {
        var account = await RegisterAsync();
        var csrf = await CsrfAsync();
        using var nativeCookie = await WebRequestAsync("/api/v1/auth/web/refresh", csrf,
            refreshCredential: account.Tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, nativeCookie.StatusCode);

        using var login = await WebRequestAsync("/api/v1/auth/web/login", csrf,
            new LoginRequest(account.Email, account.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var first = RefreshValue(login);
        using var refresh = await WebRequestAsync("/api/v1/auth/web/refresh", csrf, refreshCredential: first);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.NotEqual(first, RefreshValue(refresh));
        Assert.False((await ReadJsonAsync(refresh)).AsObject().ContainsKey("refreshToken"));
    }

    [Fact]
    public async Task WebLogoutRequiresAntiforgeryClearsCookieAndRevokesAccess()
    {
        var account = await RegisterAsync();
        var csrf = await CsrfAsync();
        using var login = await WebRequestAsync("/api/v1/auth/web/login", csrf,
            new LoginRequest(account.Email, account.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var access = (await ReadJsonAsync(login))["accessToken"]!.GetValue<string>();
        var credential = RefreshValue(login);
        csrf = await CsrfAsync(access);
        using var logout = await WebRequestAsync("/api/v1/auth/web/logout", csrf,
            refreshCredential: credential, bearer: access);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), value => value.StartsWith(RefreshCookie + "=;", StringComparison.Ordinal));
        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var after = await Client.SendAsync(me);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        csrf = await CsrfAsync();
        using var refresh = await WebRequestAsync("/api/v1/auth/web/refresh", csrf, refreshCredential: credential);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleWebAccessCannotClearOrReplaceAnotherSessionsCookie(bool selectOrganization)
    {
        var account = await RegisterAsync();
        var csrf = await CsrfAsync();
        var login = new LoginRequest(account.Email, account.Password);
        using var oldSession = await WebRequestAsync("/api/v1/auth/web/login", csrf, login);
        Assert.Equal(HttpStatusCode.OK, oldSession.StatusCode);
        var oldAccess = (await ReadJsonAsync(oldSession))["accessToken"]!.GetValue<string>();
        using var currentSession = await WebRequestAsync("/api/v1/auth/web/login", csrf, login);
        Assert.Equal(HttpStatusCode.OK, currentSession.StatusCode);
        var currentCookie = RefreshValue(currentSession);
        var protectedCsrf = await CsrfAsync(oldAccess);
        var path = selectOrganization
            ? $"/api/v1/auth/web/organizations/{account.Tokens.TenantId}/select"
            : "/api/v1/auth/web/logout";
        using var mismatch = await WebRequestAsync(path, protectedCsrf,
            refreshCredential: currentCookie, bearer: oldAccess);
        Assert.Equal(HttpStatusCode.Unauthorized, mismatch.StatusCode);
        Assert.False(mismatch.Headers.Contains("Set-Cookie"));

        using var refresh = await WebRequestAsync("/api/v1/auth/web/refresh", csrf, refreshCredential: currentCookie);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task ReplayOfWebRefreshRevokesSwitchedAccessAndRefreshCredentials()
    {
        var owner = await RegisterAsync();
        var member = await RegisterAsync();
        await AcceptMembershipAsync(owner, member, OrganizationRole.Technician);
        var anonymousCsrf = await CsrfAsync();
        using var login = await WebRequestAsync("/api/v1/auth/web/login", anonymousCsrf,
            new LoginRequest(member.Email, member.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var original = RefreshValue(login);
        using var refresh = await WebRequestAsync("/api/v1/auth/web/refresh", anonymousCsrf, refreshCredential: original);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var access = (await ReadJsonAsync(refresh))["accessToken"]!.GetValue<string>();
        using var select = await WebRequestAsync($"/api/v1/auth/web/organizations/{owner.Tokens.TenantId}/select",
            await CsrfAsync(access), refreshCredential: RefreshValue(refresh), bearer: access);
        Assert.Equal(HttpStatusCode.OK, select.StatusCode);
        var selected = await ReadJsonAsync(select);
        Assert.Equal("Technician", selected["role"]!.GetValue<string>());
        Assert.Equal(owner.Tokens.TenantId, selected["tenantId"]!.GetValue<Guid>());
        using var replay = await WebRequestAsync("/api/v1/auth/web/refresh", anonymousCsrf, refreshCredential: original);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", selected["accessToken"]!.GetValue<string>());
        using var revokedAccess = await Client.SendAsync(me);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedAccess.StatusCode);
        using var revokedRefresh = await WebRequestAsync("/api/v1/auth/web/refresh", anonymousCsrf, refreshCredential: RefreshValue(select));
        Assert.Equal(HttpStatusCode.Unauthorized, revokedRefresh.StatusCode);
    }

    [Fact]
    public async Task WebSelectionReplacesTheMatchingCookieAndRevokesPreviousAccess()
    {
        var account = await RegisterAsync();
        var csrf = await CsrfAsync();
        using var login = await WebRequestAsync("/api/v1/auth/web/login", csrf,
            new LoginRequest(account.Email, account.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var access = (await ReadJsonAsync(login))["accessToken"]!.GetValue<string>();
        var cookie = RefreshValue(login);
        var path = $"/api/v1/auth/web/organizations/{account.Tokens.TenantId}/select";
        using var selection = await WebRequestAsync(path, await CsrfAsync(access), refreshCredential: cookie, bearer: access);
        Assert.Equal(HttpStatusCode.OK, selection.StatusCode);
        var replacement = RefreshValue(selection);
        Assert.NotEqual(cookie, replacement);
        Assert.False((await ReadJsonAsync(selection)).AsObject().ContainsKey("refreshToken"));

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var previousAccess = await Client.SendAsync(me);
        Assert.Equal(HttpStatusCode.Unauthorized, previousAccess.StatusCode);
        using var refresh = await WebRequestAsync("/api/v1/auth/web/refresh", csrf, refreshCredential: replacement);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentWebSelectionAndCredentialUseRevokeTheWinner(bool raceAgainstRefresh)
    {
        var owner = await RegisterAsync();
        var member = await RegisterAsync();
        await AcceptMembershipAsync(owner, member, OrganizationRole.Technician);
        var anonymousCsrf = await CsrfAsync();
        var credentials = new LoginRequest(member.Email, member.Password);
        using var login = await WebRequestAsync("/api/v1/auth/web/login", anonymousCsrf, credentials);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var access = (await ReadJsonAsync(login))["accessToken"]!.GetValue<string>();
        var cookie = RefreshValue(login);
        var protectedCsrf = await CsrfAsync(access);

        using var independent = await WebRequestAsync("/api/v1/auth/web/login", anonymousCsrf, credentials);
        Assert.Equal(HttpStatusCode.OK, independent.StatusCode);
        var independentAccess = (await ReadJsonAsync(independent))["accessToken"]!.GetValue<string>();
        var path = $"/api/v1/auth/web/organizations/{owner.Tokens.TenantId}/select";
        var responses = await Task.WhenAll(
            WebRequestAsync(path, protectedCsrf, refreshCredential: cookie, bearer: access),
            raceAgainstRefresh
                ? WebRequestAsync("/api/v1/auth/web/refresh", anonymousCsrf, refreshCredential: cookie)
                : WebRequestAsync(path, protectedCsrf, refreshCredential: cookie, bearer: access));
        try
        {
            var winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var rejected = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
            Assert.False(rejected.Headers.Contains("Set-Cookie"));
            var winnerAccess = (await ReadJsonAsync(winner))["accessToken"]!.GetValue<string>();
            await AssertWebAccessAsync(winnerAccess, HttpStatusCode.Unauthorized);
            using var winnerRefresh = await WebRequestAsync("/api/v1/auth/web/refresh", anonymousCsrf,
                refreshCredential: RefreshValue(winner));
            Assert.Equal(HttpStatusCode.Unauthorized, winnerRefresh.StatusCode);
            await AssertWebAccessAsync(independentAccess, HttpStatusCode.OK);
            using var independentRefresh = await WebRequestAsync("/api/v1/auth/web/refresh", anonymousCsrf,
                refreshCredential: RefreshValue(independent));
            Assert.Equal(HttpStatusCode.OK, independentRefresh.StatusCode);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task StaleWebSelectionRequiresOriginAntiforgeryAndItsExactCookieBeforeRevokingTheFamily()
    {
        var account = await RegisterAsync();
        var anonymousCsrf = await CsrfAsync();
        using var login = await WebRequestAsync("/api/v1/auth/web/login", anonymousCsrf,
            new LoginRequest(account.Email, account.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var access = (await ReadJsonAsync(login))["accessToken"]!.GetValue<string>();
        var cookie = RefreshValue(login);
        var protectedCsrf = await CsrfAsync(access);
        var path = $"/api/v1/auth/web/organizations/{account.Tokens.TenantId}/select";
        using var selection = await WebRequestAsync(path, protectedCsrf, refreshCredential: cookie, bearer: access);
        Assert.Equal(HttpStatusCode.OK, selection.StatusCode);
        var successor = (await ReadJsonAsync(selection))["accessToken"]!.GetValue<string>();

        using var invalidOrigin = await WebRequestAsync(path, protectedCsrf, refreshCredential: cookie, bearer: access,
            origin: "https://untrusted.example");
        Assert.Equal(HttpStatusCode.Forbidden, invalidOrigin.StatusCode);
        using var invalidCsrf = await WebRequestAsync(path, protectedCsrf with { Token = "invalid" },
            refreshCredential: cookie, bearer: access);
        Assert.Equal(HttpStatusCode.Forbidden, invalidCsrf.StatusCode);
        using var wrongCookie = await WebRequestAsync(path, protectedCsrf, refreshCredential: RefreshValue(selection), bearer: access);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongCookie.StatusCode);
        using var nativeCookie = await WebRequestAsync(path, protectedCsrf, refreshCredential: account.Tokens.RefreshToken, bearer: access);
        Assert.Equal(HttpStatusCode.Unauthorized, nativeCookie.StatusCode);
        await AssertWebAccessAsync(successor, HttpStatusCode.OK);

        using var replay = await WebRequestAsync(path, protectedCsrf, refreshCredential: cookie, bearer: access);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.False(replay.Headers.Contains("Set-Cookie"));
        await AssertWebAccessAsync(successor, HttpStatusCode.Unauthorized);
        using var revokedRefresh = await WebRequestAsync("/api/v1/auth/web/refresh", anonymousCsrf,
            refreshCredential: RefreshValue(selection));
        Assert.Equal(HttpStatusCode.Unauthorized, revokedRefresh.StatusCode);
        await AssertWebAccessAsync(account.Tokens.AccessToken, HttpStatusCode.OK);
    }

    private async Task AssertWebAccessAsync(string access, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var response = await Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    private async Task<CsrfState> CsrfAsync(string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/web/csrf");
        request.Headers.Add("Origin", Origin);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-fieldops-csrf=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        return new CsrfState(body["token"]!.GetValue<string>(), cookie.Split(';')[0]);
    }

    private async Task<HttpResponseMessage> WebRequestAsync(string path, CsrfState csrf, object? payload = null,
        string? refreshCredential = null, string? bearer = null, string origin = Origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        request.Headers.Add("Cookie", csrf.Cookie + (refreshCredential is null ? "" : $"; {RefreshCookie}={refreshCredential}"));
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (payload is not null) request.Content = JsonContent.Create(payload, options: JsonOptions);
        return await Client.SendAsync(request);
    }

    private static string RefreshValue(HttpResponseMessage response) => CookieValue(Assert.Single(
        response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(RefreshCookie + "=", StringComparison.Ordinal)));
    private static string CookieValue(string header) => Uri.UnescapeDataString(header.Split(';')[0].Split('=', 2)[1]);
    private sealed record CsrfState(string Token, string Cookie);
}
