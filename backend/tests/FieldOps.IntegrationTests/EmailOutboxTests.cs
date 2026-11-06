using System.Net;
using System.Net.Http.Json;
using FieldOps.Application.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.IntegrationTests;

[Trait("Category", "Database")]
public sealed class EmailOutboxTests : IdentityTestBase
{
    [Fact]
    public async Task DeliveryFailureKeepsProtectedQueuedEmailAndCanRecoverWithoutFailingTheHttpRequest()
    {
        var account = await RegisterAsync();
        Emails.FailDelivery = true;
        using var requested = await Client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { account.Email });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var sent = await scope.ServiceProvider.GetRequiredService<IEmailDispatcher>().DispatchPendingAsync(CancellationToken.None);
            Assert.Equal(0, sent);
        }

        var stored = await InDatabaseAsync(context => context.OutboundEmails.SingleAsync());
        Assert.Equal(1, stored.Attempts);
        Assert.False(stored.IsDeadLetter);
        Assert.True(stored.NextAttemptAt > stored.CreatedAt);
        Assert.Equal("InvalidOperationException", stored.LastFailureType);
        Assert.DoesNotContain("test-provider-private-detail", stored.ProtectedBody);
        Assert.DoesNotContain("Token:", stored.ProtectedBody);
        Assert.DoesNotContain(account.Password, stored.ProtectedBody);

        Emails.FailDelivery = false;
        await InDatabaseAsync(async context =>
        {
            var queued = await context.OutboundEmails.SingleAsync();
            queued.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await context.SaveChangesAsync();
        });
        var delivered = await DeliveredEmailAsync(account.Email, "reset");
        Assert.NotEmpty(MessageValue(delivered, "Token:"));
        Assert.Equal(0, await InDatabaseAsync(context => context.OutboundEmails.CountAsync()));
    }
}
