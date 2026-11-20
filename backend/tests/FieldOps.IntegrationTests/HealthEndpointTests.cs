using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit.Abstractions;

namespace FieldOps.IntegrationTests;

public sealed class HealthEndpointTests(ITestOutputHelper output)
{
    [Fact]
    public async Task LivenessIsIndependentOfDatabaseAvailability()
    {
        await using var factory = new ApiFactory();
        using var response = await factory.CreateClient().GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        Assert.Empty(json.RootElement.GetProperty("checks").EnumerateArray());
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
    }

    [Fact]
    public async Task ReadinessReturnsUnavailableWithoutDisclosingConnectionDetails()
    {
        await using var factory = new ApiFactory();
        using var response = await factory.CreateClient().GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("secret-test-value", body);
        Assert.DoesNotContain("127.0.0.1", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Unhealthy", json.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    public async Task OpenApiIsAvailableOnlyInDevelopment(string environment, HttpStatusCode expected)
    {
        await using var factory = new ApiFactory(environment);
        using var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDescribesBothProbesAndReadinessFailure()
    {
        await using var factory = new ApiFactory();
        using var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var liveResponses = paths.GetProperty("/health/live").GetProperty("get").GetProperty("responses");
        var readyResponses = paths.GetProperty("/health/ready").GetProperty("get").GetProperty("responses");
        var reference = liveResponses.GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();
        Assert.Equal(reference, readyResponses.GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Equal(reference, readyResponses.GetProperty("503").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());

        var schemas = root.GetProperty("components").GetProperty("schemas");
        var properties = schemas.GetProperty(reference!.Split('/')[^1]).GetProperty("properties");
        Assert.Equal("string", properties.GetProperty("status").GetProperty("type").GetString());
        Assert.Equal("array", properties.GetProperty("checks").GetProperty("type").GetString());
        var checkReference = properties.GetProperty("checks").GetProperty("items").GetProperty("$ref").GetString();
        var check = schemas.GetProperty(checkReference!.Split('/')[^1]).GetProperty("properties");
        Assert.Equal("string", check.GetProperty("name").GetProperty("type").GetString());
        Assert.Equal("string", check.GetProperty("status").GetProperty("type").GetString());
    }

    [Fact]
    public async Task OpenApiDistinguishesProtectedRoutesAndNativeVersusWebCredentials()
    {
        await using var factory = new ApiFactory();
        using var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        foreach (var path in new[] { "/api/v1/auth/register", "/api/v1/auth/web/register", "/api/v1/organizations/{id}/invitations" })
        {
            var registration = path.EndsWith("/register", StringComparison.Ordinal);
            Assert.True(paths.GetProperty(path).GetProperty("post").GetProperty("responses").TryGetProperty(registration ? "202" : "201", out _));
        }
        var me = paths.GetProperty("/api/v1/auth/me").GetProperty("get");
        Assert.True(me.GetProperty("security")[0].TryGetProperty("Bearer", out _));
        var login = paths.GetProperty("/api/v1/auth/login").GetProperty("post");
        Assert.False(login.TryGetProperty("security", out var loginSecurity) && loginSecurity.GetArrayLength() > 0);
        var health = paths.GetProperty("/health/live").GetProperty("get");
        Assert.False(health.TryGetProperty("security", out var healthSecurity) && healthSecurity.GetArrayLength() > 0);
        var nativeReference = login.GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!;
        var webReference = paths.GetProperty("/api/v1/auth/web/login").GetProperty("post")
            .GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!;
        var schemas = root.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.GetProperty(nativeReference.Split('/')[^1]).GetProperty("properties").TryGetProperty("refreshToken", out _));
        Assert.False(schemas.GetProperty(webReference.Split('/')[^1]).GetProperty("properties").TryGetProperty("refreshToken", out _));
    }

    [Fact]
    public async Task OpenApiDocumentsOnlyApplicableProblemResponses()
    {
        await using var factory = new ApiFactory();
        using var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        AssertProblems("/api/v1/auth/register", "post", ["400", "429"], ["401", "403", "404", "409"]);
        AssertProblems("/api/v1/auth/web/login", "post", ["400", "401", "403", "429"], ["404", "409"]);
        AssertProblems("/api/v1/organizations/{id}/members/{membershipId}/role", "patch",
            ["400", "401", "403", "404", "409", "429"], []);
        AssertProblems("/health/live", "get", [], ["400", "401", "403", "404", "409", "429"]);

        void AssertProblems(string path, string method, string[] expected, string[] absent)
        {
            var responses = paths.GetProperty(path).GetProperty(method).GetProperty("responses");
            foreach (var status in expected)
                Assert.True(responses.GetProperty(status).GetProperty("content").TryGetProperty("application/problem+json", out _));
            foreach (var status in absent)
                Assert.False(responses.TryGetProperty(status, out _));
        }
    }

    [Fact]
    public async Task ReadinessTimesOutWhenDatabaseAcceptsButNeverResponds()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var accept = listener.AcceptTcpClientAsync(deadline.Token);
        await using var factory = new ApiFactory(connectionString:
            $"Host=127.0.0.1;Port={port};Database=unavailable;Username=unavailable;Password=secret-test-value;Timeout=30;Pooling=false");
        using var client = factory.CreateClient();
        var started = Stopwatch.GetTimestamp();
        var request = client.GetAsync("/health/ready", deadline.Token);
        using var connection = await accept;
        output.WriteLine($"Loopback TCP accepted: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms");
        using var response = await request;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var elapsed = Stopwatch.GetElapsedTime(started);
        output.WriteLine($"Silent database HTTP response: {elapsed.TotalMilliseconds:F1} ms");
        Assert.True(elapsed < TimeSpan.FromSeconds(5));
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-test-value", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReadinessReturnsBoundedUnavailableWhileSlowDependencyUnwinds()
    {
        var origin = Stopwatch.GetTimestamp();
        void Mark(string milestone) => output.WriteLine($"{milestone}: {Stopwatch.GetElapsedTime(origin).TotalMilliseconds:F1} ms; pending work items: {ThreadPool.PendingWorkItemCount}");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new ApiFactory().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter>(new ReadinessTimings(Mark));
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration("postgresql", _ => new SlowCheck(release, cancelled, entered, cleaned, Mark),
                    HealthStatus.Unhealthy, ["ready"]));
            });
        }));
        await using (factory)
        {
            using var client = factory.CreateClient();
            Mark("Fixture ready");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var started = Stopwatch.GetTimestamp();
            try
            {
                Mark("HTTP request issued");
                using var response = await client.GetAsync("/health/ready", deadline.Token).WaitAsync(deadline.Token);
                var elapsed = Stopwatch.GetElapsedTime(started);
                Mark("HTTP client completed");
                output.WriteLine($"Request stopwatch: {elapsed.TotalMilliseconds:F1} ms");
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.True(elapsed < TimeSpan.FromSeconds(5), $"Readiness HTTP response took {elapsed.TotalMilliseconds:F1} ms.");
                Assert.True(entered.Task.IsCompletedSuccessfully);
                Assert.False(cleaned.Task.IsCompleted);
                await cancelled.Task.WaitAsync(deadline.Token);
                using var live = await client.GetAsync("/health/live", deadline.Token);
                Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            }
            finally
            {
                Mark("Cleanup released");
                release.TrySetResult();
                if (entered.Task.IsCompletedSuccessfully) await cleaned.Task.WaitAsync(deadline.Token);
            }
        }
        Mark("Fixture disposed");
    }

    private sealed class ReadinessTimings(Action<string> mark) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => builder =>
        {
            builder.Use(async (context, pipeline) =>
            {
                if (context.Request.Path == "/health/ready")
                {
                    mark("Request reached server");
                    context.Response.OnStarting(() => { mark("Server committed response headers"); return Task.CompletedTask; });
                }
                await pipeline(context);
            });
            next(builder);
        };
    }

    private sealed class SlowCheck(TaskCompletionSource release, TaskCompletionSource cancelled, TaskCompletionSource entered,
        TaskCompletionSource cleaned, Action<string> mark) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(() => { mark("Dependency cancelled"); cancelled.TrySetResult(); });
            mark("Dependency entered (no PostgreSQL connection)");
            entered.TrySetResult();
            await release.Task;
            mark("Dependency cleanup finished");
            cleaned.TrySetResult();
            return HealthCheckResult.Unhealthy();
        }
    }

    [Fact]
    public async Task UnmatchedRoutesReturnProblemDetails()
    {
        await using var factory = new ApiFactory();
        using var response = await factory.CreateClient().GetAsync("/api/v1/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(404, json.RootElement.GetProperty("status").GetInt32());
        Assert.True(json.RootElement.TryGetProperty("traceId", out _));
    }

    [Theory]
    [InlineData("http://localhost:5173", true)]
    [InlineData("http://127.0.0.1:5173", true)]
    [InlineData("http://localhost:4173", true)]
    [InlineData("http://127.0.0.1:4173", true)]
    [InlineData("https://untrusted.example", false)]
    public async Task CorsAllowsOnlyConfiguredOrigins(string origin, bool allowed)
    {
        await using var factory = new ApiFactory();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Origin", origin);
        using var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed)
        {
            Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        }
    }
}
