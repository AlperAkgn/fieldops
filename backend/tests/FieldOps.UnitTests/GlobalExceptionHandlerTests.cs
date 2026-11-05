using System.Text.Json;
using FieldOps.Api.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FieldOps.UnitTests;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task UnexpectedErrorsReturnSafeProblemDetailsWithTraceId()
    {
        const string secret = "password=must-never-leak";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        await using var serviceProvider = services.BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
            TraceIdentifier = "request-under-test"
        };
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(
            serviceProvider.GetRequiredService<IProblemDetailsService>(),
            NullLogger<GlobalExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(context, new InvalidOperationException(secret), default);

        Assert.True(handled);
        Assert.Equal(500, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain(secret, body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("request-under-test", json.RootElement.GetProperty("traceId").GetString());
        Assert.False(json.RootElement.TryGetProperty("detail", out _));
    }
}
