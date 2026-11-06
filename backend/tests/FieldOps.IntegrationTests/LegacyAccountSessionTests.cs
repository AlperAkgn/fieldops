using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FieldOps.Application.Identity;
using FieldOps.Infrastructure.Identity;
using FieldOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FieldOps.IntegrationTests;

[Trait("Category", "Database")]
public sealed class LegacyAccountSessionTests
{
    private const string LegacyEmail = "legacy-unconfirmed@example.test";
    private const string LegacyPassword = "Legacy-Password-42!";
    private const string ChosenPassword = "Mailbox-Owner-Password-43!";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpgradeRevokesUnconfirmedLegacySessionsAndPreservesActivatedAccounts(bool lineageAlreadyApplied)
    {
        await using var testDatabase = await TestDatabase.CreateAsync();
        await using var factory = new ApiFactory(connectionString: testDatabase.ConnectionString);
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
        var migrator = database.GetService<IMigrator>();
        var migrations = database.Database.GetMigrations().ToArray();
        var identity = Assert.Single(migrations, id => id.EndsWith("_IdentityAndOrganizations", StringComparison.Ordinal));
        await migrator.MigrateAsync(identity);

        var users = scope.ServiceProvider.GetRequiredService<UserManager<FieldOpsUser>>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<FieldOpsUser>>();
        var legacyPasswordHash = hasher.HashPassword(new FieldOpsUser(), LegacyPassword);
        var unconfirmed = Guid.NewGuid();
        var confirmed = Guid.NewGuid();
        var noPassword = Guid.NewGuid();
        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();
        var unconfirmedSession = Guid.NewGuid();
        var confirmedSession = Guid.NewGuid();
        var noPasswordSession = Guid.NewGuid();
        var unconfirmedRefresh = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        var confirmedRefresh = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        var unconfirmedHash = HashRefresh(unconfirmedRefresh);
        var confirmedHash = HashRefresh(confirmedRefresh);
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AspNetUsers" ("Id", "DisplayName", "IsActive", "CreatedAt", "UserName", "NormalizedUserName",
                "Email", "NormalizedEmail", "EmailConfirmed", "PasswordHash", "SecurityStamp", "PhoneNumberConfirmed",
                "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ({unconfirmed}, 'Unconfirmed', true, now(), {LegacyEmail}, {LegacyEmail.ToUpperInvariant()},
                    {LegacyEmail}, {LegacyEmail.ToUpperInvariant()}, false, {legacyPasswordHash}, 'legacy-unconfirmed-stamp', false, false, true, 0),
                   ({confirmed}, 'Confirmed', true, now(), 'confirmed@example.test', 'CONFIRMED@EXAMPLE.TEST',
                    'confirmed@example.test', 'CONFIRMED@EXAMPLE.TEST', true, {legacyPasswordHash}, 'legacy-confirmed-stamp', false, false, true, 0),
                   ({noPassword}, 'No password', true, now(), 'no-password@example.test', 'NO-PASSWORD@EXAMPLE.TEST',
                    'no-password@example.test', 'NO-PASSWORD@EXAMPLE.TEST', true, NULL, 'legacy-no-password-stamp', false, false, true, 0);
            INSERT INTO "Organizations" ("Id", "Name", "TimeZoneId", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES ({firstTenant}, 'Legacy first', 'UTC', true, now(), now()),
                   ({secondTenant}, 'Legacy second', 'UTC', true, now(), now());
            INSERT INTO "OrganizationMemberships" ("Id", "TenantId", "UserId", "Role", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES (gen_random_uuid(), {firstTenant}, {unconfirmed}, 'OrganizationOwner', true, now(), now()),
                   (gen_random_uuid(), {firstTenant}, {confirmed}, 'OrganizationOwner', true, now(), now()),
                   (gen_random_uuid(), {firstTenant}, {noPassword}, 'Technician', true, now(), now()),
                   (gen_random_uuid(), {secondTenant}, {unconfirmed}, 'Technician', true, now(), now()),
                   (gen_random_uuid(), {secondTenant}, {confirmed}, 'OrganizationOwner', true, now(), now());
            INSERT INTO "AuthSessions" ("Id", "UserId", "TenantId", "Transport", "SecurityStamp", "CreatedAt", "LastUsedAt", "ExpiresAt")
            VALUES ({unconfirmedSession}, {unconfirmed}, {firstTenant}, 'Native', 'legacy-unconfirmed-stamp', now(), now(), now() + interval '1 day'),
                   ({confirmedSession}, {confirmed}, {firstTenant}, 'Native', 'legacy-confirmed-stamp', now(), now(), now() + interval '1 day'),
                   ({noPasswordSession}, {noPassword}, {firstTenant}, 'Native', 'legacy-no-password-stamp', now(), now(), now() + interval '1 day');
            INSERT INTO "RefreshCredentials" ("Id", "SessionId", "TokenHash", "CreatedAt", "ExpiresAt")
            VALUES (gen_random_uuid(), {unconfirmedSession}, {unconfirmedHash}, now(), now() + interval '1 day'),
                   (gen_random_uuid(), {confirmedSession}, {confirmedHash}, now(), now() + interval '1 day');
            """);
        var jwtOptions = scope.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>().Value;
        var oldUnconfirmedAccess = IssueAccess(jwtOptions, unconfirmed, unconfirmedSession, firstTenant);
        var oldConfirmedAccess = IssueAccess(jwtOptions, confirmed, confirmedSession, firstTenant);
        if (lineageAlreadyApplied)
        {
            var lineage = Assert.Single(migrations, id => id.EndsWith("_RefreshSessionLineage", StringComparison.Ordinal));
            await migrator.MigrateAsync(lineage);
        }
        await database.Database.MigrateAsync();
        await database.Database.MigrateAsync();
        Assert.Equal(migrations, await database.Database.GetAppliedMigrationsAsync());

        var legacySessions = await database.AuthSessions.AsNoTracking().ToDictionaryAsync(session => session.Id);
        Assert.Equal(3, legacySessions.Count);
        Assert.NotNull(legacySessions[unconfirmedSession].RevokedAt);
        Assert.Equal("account_activation_required", legacySessions[unconfirmedSession].RevocationReason);
        Assert.NotNull(legacySessions[noPasswordSession].RevokedAt);
        Assert.Equal("account_activation_required", legacySessions[noPasswordSession].RevocationReason);
        Assert.Null(legacySessions[confirmedSession].RevokedAt);
        Assert.Equal(3, await database.Users.CountAsync());
        Assert.Equal(2, await database.Organizations.IgnoreQueryFilters().CountAsync());
        Assert.Equal(5, await database.OrganizationMemberships.IgnoreQueryFilters().CountAsync());
        var credentials = await database.RefreshCredentials.AsNoTracking().ToDictionaryAsync(credential => credential.SessionId);
        Assert.Equal(unconfirmedHash, credentials[unconfirmedSession].TokenHash);
        Assert.Equal(confirmedHash, credentials[confirmedSession].TokenHash);

        using var deniedAccess = await SendAuthorizedAsync(client, HttpMethod.Get, "/api/v1/auth/me", oldUnconfirmedAccess);
        Assert.Equal(HttpStatusCode.Unauthorized, deniedAccess.StatusCode);
        using var deniedSelection = await SendAuthorizedAsync(client, HttpMethod.Post,
            $"/api/v1/organizations/{secondTenant}/select", oldUnconfirmedAccess, new { refreshToken = unconfirmedRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, deniedSelection.StatusCode);
        using var deniedRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = unconfirmedRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, deniedRefresh.StatusCode);
        using var deniedLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(LegacyEmail, LegacyPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, deniedLogin.StatusCode);

        using var confirmedAccess = await SendAuthorizedAsync(client, HttpMethod.Get, "/api/v1/auth/me", oldConfirmedAccess);
        Assert.Equal(HttpStatusCode.OK, confirmedAccess.StatusCode);
        using var renewedConfirmed = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = confirmedRefresh });
        Assert.Equal(HttpStatusCode.OK, renewedConfirmed.StatusCode);

        var pending = await users.FindByIdAsync(unconfirmed.ToString());
        Assert.NotNull(pending);
        Assert.Equal(legacyPasswordHash, pending.PasswordHash);
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(pending)));
        using var activation = await client.PostAsJsonAsync("/api/v1/auth/confirm-email",
            new { userId = unconfirmed, token, password = ChosenPassword });
        Assert.Equal(HttpStatusCode.NoContent, activation.StatusCode);
        using var activatedLogin = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(LegacyEmail, ChosenPassword, firstTenant));
        Assert.Equal(HttpStatusCode.OK, activatedLogin.StatusCode);
        var activated = (await activatedLogin.Content.ReadFromJsonAsync<TokenPair>(JsonOptions))!;
        Assert.Equal(firstTenant, activated.TenantId);
        using var active = await SendAuthorizedAsync(client, HttpMethod.Get, "/api/v1/auth/me", activated.AccessToken);
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.True((await active.Content.ReadFromJsonAsync<CurrentUserDto>(JsonOptions))!.EmailConfirmed);
        using var oldRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = unconfirmedRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        using var oldAccess = await SendAuthorizedAsync(client, HttpMethod.Get, "/api/v1/auth/me", oldUnconfirmedAccess);
        Assert.Equal(HttpStatusCode.Unauthorized, oldAccess.StatusCode);

        using var deactivation = await SendAuthorizedAsync(client, HttpMethod.Post, "/api/v1/auth/deactivate", activated.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, deactivation.StatusCode);
        using var deactivatedAccess = await SendAuthorizedAsync(client, HttpMethod.Get, "/api/v1/auth/me", activated.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, deactivatedAccess.StatusCode);
        using var deactivatedRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { activated.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, deactivatedRefresh.StatusCode);
    }

    private static string HashRefresh(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private static string IssueAccess(JwtOptions options, Guid user, Guid session, Guid tenant) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.ToString(),
                ["sid"] = session.ToString(),
                ["tenant_id"] = tenant.ToString()
            },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        });

    private static async Task<HttpResponseMessage> SendAuthorizedAsync(HttpClient client, HttpMethod method, string path,
        string access, object? payload = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        return await client.SendAsync(request);
    }
}
