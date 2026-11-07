using System.Net;
using System.Text.Json;

namespace FieldOps.IntegrationTests;

public sealed class OperationalApiContractTests
{
    [Theory]
    [InlineData("/api/v1/customers")]
    [InlineData("/api/v1/service-locations")]
    [InlineData("/api/v1/assets")]
    [InlineData("/api/v1/asset-categories")]
    [InlineData("/api/v1/work-orders")]
    [InlineData("/api/v1/checklist-templates")]
    [InlineData("/api/v1/audit-logs")]
    public async Task OperationalReadsChallengeAnonymousCallers(string path)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OperationalOpenApiIncludesBusinessActionsAndConflictContracts()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var schedule = paths.GetProperty("/api/v1/work-orders/{id}/schedule").GetProperty("post");
        Assert.True(schedule.GetProperty("security")[0].TryGetProperty("Bearer", out _));
        Assert.True(schedule.GetProperty("responses").GetProperty("409").GetProperty("content")
            .TryGetProperty("application/problem+json", out _));
        Assert.True(paths.TryGetProperty("/api/v1/audit-logs", out _));
    }
}
