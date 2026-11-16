using System.Net;
using System.Text.Json;
using FieldOps.Application.Collaboration;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.IntegrationTests;

public sealed class ExpoPushProviderTests
{
    [Theory]
    [InlineData(null, "{\"data\":{\"status\":\"ok\",\"id\":\"ticket-1\"}}", "ticket-1", null, false)]
    [InlineData(null, "{\"data\":[{\"status\":\"ok\",\"id\":\"ticket-1\"}]}", "ticket-1", null, false)]
    [InlineData("ticket-1", "{\"data\":{}}", null, "provider_result_pending", true)]
    [InlineData("ticket-1", "{\"data\":[]}", null, "provider_result_pending", true)]
    [InlineData("ticket-1", "{\"data\":{\"ticket-1\":{\"status\":\"ok\"}}}", "ticket-1", null, false)]
    [InlineData("ticket-1", "{\"data\":{\"ticket-1\":{\"status\":\"error\",\"details\":{\"error\":\"DeviceNotRegistered\"}}}}", null, "device_not_registered", false)]
    public async Task HandlesDocumentedTicketsAndUnavailableReceipts(string? ticket, string response,
        string? expectedTicket, string? error, bool retryable)
    {
        const string deviceToken = "ExpoPushToken[fixture-device]";
        var notification = Guid.NewGuid();
        using var root = new ApiFactory(settings: new Dictionary<string, string?> { ["ExpoPush:AccessToken"] = "test-access-token" });
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient(nameof(IPushProvider)).ConfigurePrimaryHttpMessageHandler(() => new ReplyHandler(response, async request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                if (ticket is null)
                {
                    Assert.EndsWith("/push/send", request.RequestUri!.AbsolutePath);
                    Assert.Equal(deviceToken, body.RootElement.GetProperty("to").GetString());
                    Assert.Equal(notification, body.RootElement.GetProperty("data").GetProperty("notificationId").GetGuid());
                    Assert.Equal(notification.ToString(), body.RootElement.GetProperty("collapseId").GetString());
                }
                else
                {
                    Assert.EndsWith("/push/getReceipts", request.RequestUri!.AbsolutePath);
                    Assert.Equal(ticket, Assert.Single(body.RootElement.GetProperty("ids").EnumerateArray()).GetString());
                }
            }))));
        var provider = factory.Services.GetRequiredService<IPushProvider>();
        Assert.True(provider.Enabled);
        var result = ticket is null
            ? await provider.SendAsync(deviceToken, notification, CancellationToken.None)
            : await provider.ReceiptAsync(ticket, CancellationToken.None);
        Assert.Equal(new PushResult(expectedTicket, error, retryable), result);
    }

    private sealed class ReplyHandler(string response, Func<HttpRequestMessage, Task> inspect) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await inspect(request);
            return new(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }
}
