using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace FieldOps.IntegrationTests;

public sealed class HealthEndpointTests
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
    public async Task ReadinessTimesOutWhenDatabaseAcceptsButNeverResponds()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var accept = listener.AcceptTcpClientAsync(deadline.Token);
        await using var factory = new ApiFactory(connectionString:
            $"Host=127.0.0.1;Port={port};Database=unavailable;Username=unavailable;Password=secret-test-value;Timeout=30;Pooling=false");
        var request = factory.CreateClient().GetAsync("/health/ready", deadline.Token);
        using var connection = await accept;
        using var response = await request;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-test-value", await response.Content.ReadAsStringAsync());
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
