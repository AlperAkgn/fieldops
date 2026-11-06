using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FieldOps.Application.Identity;
using FieldOps.Application.Organizations;
using FieldOps.Domain.Organizations;
using FieldOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FieldOps.IntegrationTests;

public abstract class IdentityTestBase : IAsyncLifetime
{
    private TestDatabase? database;
    protected WebApplicationFactory<Program> Factory { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;
    protected RecordingEmailDelivery Emails { get; } = new();
    protected string DatabaseConnectionString => database!.ConnectionString;
    protected static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task InitializeAsync()
    {
        database = await TestDatabase.CreateAsync();
        Factory = new ApiFactory(connectionString: database.ConnectionString).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailDelivery>();
                services.AddSingleton<IEmailDelivery>(Emails);
            }));
        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        await InDatabaseAsync(async context => await context.Database.MigrateAsync());
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        if (database is not null)
        {
            await database.DisposeAsync();
        }
    }

    protected async Task<TestAccount> RegisterAsync(string? email = null)
    {
        var pending = await BeginRegistrationAsync(email);
        await ConfirmRegistrationAsync(pending);
        return new TestAccount(pending.Email, pending.Password, await LoginAsync(pending.Email, pending.Password));
    }

    protected async Task<PendingAccount> BeginRegistrationAsync(string? email = null)
    {
        email ??= $"user-{Guid.NewGuid():N}@example.test";
        var request = new RegisterRequest(email, "Test User", "Test Organization", "Europe/Istanbul");
        using var response = await Client.PostAsJsonAsync("/api/v1/auth/register", request, JsonOptions);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return new PendingAccount(email, "Valid-Test-Password-42!");
    }

    protected Task<TokenPair> LoginAsync(TestAccount account) => LoginAsync(account.Email, account.Password);

    private async Task<TokenPair> LoginAsync(string email, string password)
    {
        using var response = await Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(email, password), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenPair>(JsonOptions))!;
    }

    protected async Task ConfirmRegistrationAsync(PendingAccount account)
    {
        var message = await DeliveredEmailAsync(account.Email, "confirm");
        using var response = await Client.PostAsJsonAsync("/api/v1/auth/confirm-email", new
        {
            userId = Guid.Parse(MessageValue(message, "User ID:")),
            token = MessageValue(message, "Token:"),
            account.Password
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    protected async Task<TokenPair> JoinAsync(TestAccount owner, TestAccount member, OrganizationRole role)
    {
        await AcceptMembershipAsync(owner, member, role);
        using var select = await AuthorizedAsync(HttpMethod.Post, $"/api/v1/organizations/{owner.Tokens.TenantId}/select",
            member.Tokens, new { member.Tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, select.StatusCode);
        return (await select.Content.ReadFromJsonAsync<TokenPair>(JsonOptions))!;
    }

    protected async Task AcceptMembershipAsync(TestAccount owner, TestAccount member, OrganizationRole role)
    {
        using var invitation = await AuthorizedAsync(HttpMethod.Post, $"/api/v1/organizations/{owner.Tokens.TenantId}/invitations",
            owner.Tokens, new CreateInvitationRequest(member.Email, role));
        Assert.Equal(HttpStatusCode.Created, invitation.StatusCode);
        var message = await DeliveredEmailAsync(member.Email, "invitation");
        using var accepted = await AuthorizedAsync(HttpMethod.Post, "/api/v1/invitations/accept", member.Tokens,
            new AcceptInvitationRequest(MessageValue(message, "Token:")));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    protected async Task<Guid> MembershipIdAsync(TokenPair owner, Guid userId)
    {
        using var response = await AuthorizedAsync(HttpMethod.Get, $"/api/v1/organizations/{owner.TenantId}/members", owner);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var members = (await response.Content.ReadFromJsonAsync<OrganizationMemberDto[]>(JsonOptions))!;
        return Assert.Single(members, member => member.UserId == userId).Id;
    }

    protected Task<HttpResponseMessage> AuthorizedAsync(HttpMethod method, string path, TokenPair tokens, object? payload = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        return SendAndDisposeRequestAsync(request);
    }

    protected async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonNode>(JsonOptions))!;

    protected async Task InDatabaseAsync(Func<FieldOpsDbContext, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>());
    }

    protected async Task<T> InDatabaseAsync<T>(Func<FieldOpsDbContext, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>());
    }

    protected async Task<EmailMessage> DeliveredEmailAsync(string email, string subjectFragment)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IEmailDispatcher>().DispatchPendingAsync(CancellationToken.None);
            var message = Emails.Messages.LastOrDefault(message =>
                message.ToEmail.Equals(email, StringComparison.OrdinalIgnoreCase)
                && message.Subject.Contains(subjectFragment, StringComparison.OrdinalIgnoreCase));
            if (message is not null)
            {
                return message;
            }

            await Task.Delay(20);
        }

        throw new Xunit.Sdk.XunitException("The expected test email was not delivered to the recording adapter.");
    }

    protected static string MessageValue(EmailMessage message, string prefix)
        => message.Body.Split('\n').Single(line => line.StartsWith(prefix, StringComparison.Ordinal))
            [prefix.Length..].Trim();

    private async Task<HttpResponseMessage> SendAndDisposeRequestAsync(HttpRequestMessage request)
    {
        using (request)
        {
            return await Client.SendAsync(request);
        }
    }

    protected sealed record TestAccount(string Email, string Password, TokenPair Tokens);
    protected sealed record PendingAccount(string Email, string Password);

    protected sealed class RecordingEmailDelivery : IEmailDelivery
    {
        public ConcurrentQueue<EmailMessage> Messages { get; } = new();
        public bool FailDelivery { get; set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailDelivery) throw new InvalidOperationException("test-provider-private-detail");
            Messages.Enqueue(message);
            return Task.CompletedTask;
        }
    }
}
