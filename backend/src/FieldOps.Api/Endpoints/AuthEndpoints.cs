using FieldOps.Api.Security;
using FieldOps.Application.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FieldOps.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/v1/auth").WithTags("Authentication").WithProblemResponses(429);
        auth.MapPost("/register", async (RegisterRequest request, HttpContext http, IIdentityService service, CancellationToken ct) =>
        {
            ApiSecurity.NativeRequest(http);
            await service.RegisterAsync(request, ct);
            return RegistrationAccepted();
        }).AllowAnonymous().WithProblemResponses(400);
        auth.MapPost("/login", async (LoginRequest request, HttpContext http, IIdentityService service, CancellationToken ct) =>
        {
            ApiSecurity.NativeRequest(http);
            return TypedResults.Ok(await service.LoginAsync(request, SessionTransport.Native, ct));
        }).AllowAnonymous().WithProblemResponses(400, 401);
        auth.MapPost("/refresh", async (RefreshRequest request, HttpContext http, IIdentityService service, CancellationToken ct) =>
        {
            ApiSecurity.NativeRequest(http);
            return TypedResults.Ok(await service.RefreshAsync(request.RefreshToken, SessionTransport.Native, ct));
        }).AllowAnonymous().WithProblemResponses(400, 401);

        auth.MapGet("/web/csrf", (HttpContext http, IAntiforgery antiforgery) =>
        {
            ApiSecurity.RequireWebHttps(http);
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(new { token = antiforgery.GetAndStoreTokens(http).RequestToken });
        }).AllowAnonymous().WithProblemResponses(400);
        auth.MapPost("/web/register", async (RegisterRequest request, HttpContext http, IIdentityService service, CancellationToken ct) =>
        {
            await ApiSecurity.WebRequestAsync(http);
            await service.RegisterAsync(request, ct);
            return RegistrationAccepted();
        }).AllowAnonymous().WithProblemResponses(400, 403);
        auth.MapPost("/web/login", async (LoginRequest request, HttpContext http, IIdentityService service, CancellationToken ct) =>
        {
            await ApiSecurity.WebRequestAsync(http);
            var pair = await service.LoginAsync(request, SessionTransport.Web, ct);
            ApiSecurity.SetRefreshCookie(http, pair);
            return TypedResults.Ok(AccessTokenResponse.From(pair));
        }).AllowAnonymous().WithProblemResponses(400, 401, 403);
        auth.MapPost("/web/refresh", async (HttpContext http, IIdentityService service, CancellationToken ct) =>
        {
            await ApiSecurity.WebRequestAsync(http);
            var pair = await service.RefreshAsync(http.Request.Cookies[ApiSecurity.RefreshCookie] ?? "", SessionTransport.Web, ct);
            ApiSecurity.SetRefreshCookie(http, pair);
            return TypedResults.Ok(AccessTokenResponse.From(pair));
        }).AllowAnonymous().WithProblemResponses(400, 401, 403);

        auth.MapGet("/me", async (HttpContext http, IIdentityService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetCurrentUserAsync(ApiSecurity.Actor(http), ct))).RequireAuthorization().WithProblemResponses(401);
        auth.MapPost("/logout", async (HttpContext http, IIdentityService service, CancellationToken ct) =>
        {
            await service.LogoutAsync(ApiSecurity.Actor(http), ct);
            return TypedResults.NoContent();
        }).RequireAuthorization().WithProblemResponses(401);
        auth.MapPost("/web/logout", async (HttpContext http, IIdentityService service, CancellationToken ct) =>
        {
            await ApiSecurity.WebRequestAsync(http);
            await service.LogoutWebAsync(ApiSecurity.Actor(http), http.Request.Cookies[ApiSecurity.RefreshCookie] ?? "", ct);
            ApiSecurity.ClearRefreshCookie(http);
            return TypedResults.NoContent();
        }).RequireAuthorization().WithProblemResponses(400, 401, 403);
        auth.MapGet("/sessions", async (HttpContext http, IIdentityService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetSessionsAsync(ApiSecurity.Actor(http), ct))).RequireAuthorization().WithProblemResponses(401);
        auth.MapDelete("/sessions/{id:guid}", async (Guid id, HttpContext http, IIdentityService service, CancellationToken ct) =>
        {
            await service.RevokeSessionAsync(ApiSecurity.Actor(http), id, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization().WithProblemResponses(401, 404);
        auth.MapPost("/logout-all", async (HttpContext http, IIdentityService service, CancellationToken ct) =>
        {
            await service.RevokeAllSessionsAsync(ApiSecurity.Actor(http), ct);
            return TypedResults.NoContent();
        }).RequireAuthorization().WithProblemResponses(401);
        auth.MapPost("/deactivate", async (HttpContext http, IIdentityService service, CancellationToken ct) =>
        {
            await service.DeactivateAccountAsync(ApiSecurity.Actor(http), ct);
            return TypedResults.NoContent();
        }).RequireAuthorization().WithProblemResponses(401, 409);
        auth.MapPost("/web/organizations/{id:guid}/select", async (Guid id, HttpContext http, IIdentityService service, CancellationToken ct) =>
        {
            await ApiSecurity.WebRequestAsync(http);
            var pair = await service.SelectOrganizationAsync(ApiSecurity.SelectionSession(http), id, SessionTransport.Web,
                http.Request.Cookies[ApiSecurity.RefreshCookie] ?? "", ct);
            ApiSecurity.SetRefreshCookie(http, pair);
            return TypedResults.Ok(AccessTokenResponse.From(pair));
        }).RequireAuthorization().WithMetadata(new OrganizationSelectionAuthentication()).WithProblemResponses(400, 401, 403, 404);

        auth.MapPost("/forgot-password", async (EmailRequest request, IIdentityService service, CancellationToken ct) =>
        {
            await service.RequestPasswordResetAsync(request.Email, ct);
            return TypedResults.Accepted(uri: (string?)null, value: new { message = "If the account is eligible, a password reset message has been queued." });
        }).AllowAnonymous().WithProblemResponses(400);
        auth.MapPost("/reset-password", async (ResetPasswordRequest request, IIdentityService service, CancellationToken ct) =>
        {
            await service.ResetPasswordAsync(request.UserId, request.Token, request.NewPassword, ct);
            return TypedResults.NoContent();
        }).AllowAnonymous().WithProblemResponses(400);
        auth.MapPost("/request-email-confirmation", async (EmailRequest request, IIdentityService service, CancellationToken ct) =>
        {
            await service.RequestEmailConfirmationAsync(request.Email, ct);
            return TypedResults.Accepted(uri: (string?)null, value: new { message = "If the account is eligible, a confirmation message has been queued." });
        }).AllowAnonymous().WithProblemResponses(400);
        auth.MapPost("/confirm-email", async (ConfirmEmailRequest request, IIdentityService service, CancellationToken ct) =>
        {
            await service.ConfirmEmailAsync(request.UserId, request.Token, request.Password, ct);
            return TypedResults.NoContent();
        }).AllowAnonymous().WithProblemResponses(400);
    }

    private static Accepted<RegistrationResponse> RegistrationAccepted() => TypedResults.Accepted(uri: (string?)null,
        value: new RegistrationResponse("If the address is eligible, an email with confirmation instructions has been queued."));
}

public sealed record RefreshRequest(string RefreshToken);
public sealed record EmailRequest(string Email);
public sealed record ResetPasswordRequest(Guid UserId, string Token, string NewPassword);
public sealed record ConfirmEmailRequest(Guid UserId, string Token, string Password);
public sealed record RegistrationResponse(string Message);
public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAt, DateTimeOffset RefreshExpiresAt,
    Guid SessionId, Guid TenantId, FieldOps.Domain.Organizations.OrganizationRole Role, Guid UserId)
{
    public static AccessTokenResponse From(TokenPair pair) => new(pair.AccessToken, pair.ExpiresAt,
        pair.RefreshExpiresAt, pair.SessionId, pair.TenantId, pair.Role, pair.UserId);
}
