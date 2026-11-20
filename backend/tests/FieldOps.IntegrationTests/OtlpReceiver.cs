using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FieldOps.IntegrationTests;

internal sealed class OtlpReceiver(WebApplication app) : IAsyncDisposable
{
    public string Origin => Assert.Single(app.Urls);
    public ConcurrentQueue<(string Signal, string Payload)> Payloads { get; } = new();

    public static async Task<OtlpReceiver> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var receiver = new OtlpReceiver(app);
        app.MapPost("/v1/{signal}", async (string signal, HttpRequest request) =>
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, request.HttpContext.RequestAborted);
            receiver.Payloads.Enqueue((signal, Encoding.UTF8.GetString(buffer.ToArray())));
            return Results.Bytes([], "application/x-protobuf");
        });
        await app.StartAsync();
        return receiver;
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
