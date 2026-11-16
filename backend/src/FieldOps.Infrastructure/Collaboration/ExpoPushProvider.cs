using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FieldOps.Application.Collaboration;
using Microsoft.Extensions.Configuration;

namespace FieldOps.Infrastructure.Collaboration;

internal sealed class ExpoPushProvider(HttpClient client, IConfiguration configuration) : IPushProvider
{
    private readonly string? accessToken = configuration["ExpoPush:AccessToken"];
    public bool Enabled => !string.IsNullOrWhiteSpace(accessToken);

    public Task<PushResult> SendAsync(string token, Guid notificationId, CancellationToken cancellationToken) =>
        RequestAsync("send", new
        {
            to = token,
            title = "FieldOps",
            body = "Open FieldOps to see a workspace update.",
            channelId = "work-updates",
            collapseId = notificationId.ToString(),
            data = new { notificationId }
        }, null, cancellationToken);

    public Task<PushResult> ReceiptAsync(string ticketId, CancellationToken cancellationToken) =>
        RequestAsync("getReceipts", new { ids = new[] { ticketId } }, ticketId, cancellationToken);

    private async Task<PushResult> RequestAsync(string path, object body, string? ticket, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://exp.host/--/api/v2/push/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new(null, "provider_http_error", (int)response.StatusCode is 429 or >= 500);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("data", out var result))
            return new(null, "provider_result_pending", true);
        if (ticket is not null && (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(ticket, out result)))
            return new(null, "provider_result_pending", true);
        if (ticket is null && result.ValueKind == JsonValueKind.Array && result.GetArrayLength() == 1)
            result = result[0];
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("status", out var status) ||
            status.ValueKind != JsonValueKind.String)
            return new(null, "provider_result_pending", true);
        if (status.GetString() == "ok")
        {
            if (ticket is not null) return new(ticket, null, false);
            var id = result.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            return id is { Length: > 0 and <= 128 } ? new(id, null, false) : new(null, "provider_ticket_missing", false);
        }
        var error = result.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object &&
            details.TryGetProperty("error", out var code) && code.ValueKind == JsonValueKind.String
            ? code.GetString() : null;
        return error switch
        {
            "DeviceNotRegistered" => new(null, "device_not_registered", false),
            "MessageRateExceeded" => new(null, "provider_rate_limited", true),
            _ => new(null, "provider_rejected", false)
        };
    }
}
