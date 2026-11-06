using System.Net;
using System.Net.Http.Json;
using FieldOps.Application.Identity;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.IntegrationTests;

[Trait("Category", "Database")]
public sealed class LoginInputSecurityTests : IdentityTestBase
{
    [Theory]
    [InlineData(false, "bad\0email@example.test")]
    [InlineData(true, "bad\0email@example.test")]
    [InlineData(false, "valid@example.test\t")]
    [InlineData(true, "bad\u0085email@example.test")]
    public async Task LoginRejectsControlCharactersWithBadRequest(bool web, string email)
    {
        using var response = await SubmitLoginAsync(web, new LoginRequest(email, "Valid-Test-Password-42!"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_email", (await ReadJsonAsync(response))["code"]!.GetValue<string>());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoginPreservesAnExistingUnicodeEmail(bool web)
    {
        var account = await RegisterAsync();
        const string email = "müşteri@örnek.test";
        await InDatabaseAsync(async database =>
        {
            var user = await database.Users.SingleAsync(item => item.Id == account.Tokens.UserId);
            user.Email = email;
            user.NormalizedEmail = email.Normalize().ToUpperInvariant();
            await database.SaveChangesAsync();
        });

        using var response = await SubmitLoginAsync(web, new LoginRequest(email, account.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(account.Tokens.UserId, (await ReadJsonAsync(response))["userId"]!.GetValue<Guid>());
    }

    private async Task<HttpResponseMessage> SubmitLoginAsync(bool web, LoginRequest payload)
    {
        if (!web)
        {
            return await Client.PostAsJsonAsync("/api/v1/auth/login", payload, JsonOptions);
        }

        using var csrf = await Client.GetAsync("/api/v1/auth/web/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        var cookie = Assert.Single(csrf.Headers.GetValues("Set-Cookie"));
        var token = (await ReadJsonAsync(csrf))["token"]!.GetValue<string>();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/web/login")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        login.Headers.Add("Origin", "https://localhost:5173");
        login.Headers.Add("X-CSRF-TOKEN", token);
        login.Headers.Add("Cookie", cookie.Split(';')[0]);
        return await Client.SendAsync(login);
    }
}
