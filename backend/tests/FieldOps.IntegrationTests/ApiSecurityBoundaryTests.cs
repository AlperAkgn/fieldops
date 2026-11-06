using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FieldOps.IntegrationTests;

public sealed class ApiSecurityBoundaryTests
{
    [Theory]
    [InlineData("/api/v1/auth/me")]
    [InlineData("/api/v1/auth/sessions")]
    [InlineData("/api/v1/organizations")]
    public async Task ProtectedRoutesChallengeAnonymousCallerWithoutDatabaseAccess(string path)
    {
        await using var factory = new ApiFactory();
        using var response = await factory.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("secret-test-value", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AuthenticationRateLimitReturnsSafeProblemAndRetryAfterWhileHealthRemainsAvailable()
    {
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?>
        {
            ["RateLimits:AuthPermitLimit"] = "1"
        });
        using var client = factory.CreateClient();
        using var first = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "invalid-address" });
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        using var blocked = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "invalid-address" });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal("application/problem+json", blocked.Content.Headers.ContentType?.MediaType);
        Assert.True(blocked.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        using var problem = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        Assert.Equal("rate_limit_exceeded", problem.RootElement.GetProperty("code").GetString());
        using var health = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
