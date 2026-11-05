using System.Text.Json;
using FieldOps.Api.Health;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FieldOps.UnitTests;

public sealed class HealthResponseWriterTests
{
    [Fact]
    public async Task FailureResponseDoesNotDiscloseDiagnosticData()
    {
        const string secret = "password=must-never-leak";
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["postgresql"] = new(HealthStatus.Unhealthy, secret, TimeSpan.Zero,
                new InvalidOperationException(secret), new Dictionary<string, object> { ["connection"] = secret })
        };
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await HealthResponseWriter.WriteAsync(context, new HealthReport(entries, TimeSpan.Zero));
        context.Response.Body.Position = 0;
        var response = await new StreamReader(context.Response.Body).ReadToEndAsync();

        Assert.DoesNotContain(secret, response);
        using var json = JsonDocument.Parse(response);
        Assert.Equal("Unhealthy", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("checks")[0].EnumerateObject().Count());
    }
}
