using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FieldOps.IntegrationTests;

public sealed class ExceptionHandlingTests
{
    [Theory]
    [InlineData("application/json")]
    [InlineData("text/html")]
    public async Task UnhandledErrorsPreserveCorrelationAndReturnSafeProblemDetails(string accept)
    {
        var logs = new ConcurrentQueue<string>();
        await using var factory = new ApiFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(new CapturingLoggerProvider(logs)));
            builder.ConfigureTestServices(services =>
                services.Insert(0, ServiceDescriptor.Singleton<IProblemDetailsWriter, ThrowingNotFoundWriter>()));
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/does-not-exist");
        request.Headers.Accept.ParseAdd(accept);
        using var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ThrowingNotFoundWriter.Secret, body);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(500, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(problem.RootElement.TryGetProperty("detail", out _));
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        Assert.Equal(Assert.Single(response.Headers.GetValues("X-Correlation-ID")),
            problem.RootElement.GetProperty("traceId").GetString());
        Assert.Contains(logs, message => message.Contains("HTTP GET completed 500", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, message => message.Contains(ThrowingNotFoundWriter.Secret, StringComparison.Ordinal));
    }

    private sealed class ThrowingNotFoundWriter : IProblemDetailsWriter
    {
        public const string Secret = "internal-credential-must-never-leak";

        public bool CanWrite(ProblemDetailsContext context) => context.HttpContext.Response.StatusCode == 404;

        public ValueTask WriteAsync(ProblemDetailsContext context) => throw new InvalidOperationException(Secret);
    }

    private sealed class CapturingLoggerProvider(ConcurrentQueue<string> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(logs);

        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => logs.Enqueue(formatter(state, exception) + exception);
        }
    }
}
