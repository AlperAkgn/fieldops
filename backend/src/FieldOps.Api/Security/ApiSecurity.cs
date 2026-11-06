using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using FieldOps.Application.Common;
using FieldOps.Application.Identity;
using FieldOps.Application.Tenancy;
using FieldOps.Domain.Organizations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace FieldOps.Api.Security;

public static class ApiSecurity
{
    public const string RefreshCookie = "__Secure-fieldops-refresh";
    public const string WebPath = "/api/v1/auth/web";

    public static IServiceCollection AddApiSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 64 || string.IsNullOrWhiteSpace(jwt.Issuer)
            || string.IsNullOrWhiteSpace(jwt.Audience))
        {
            throw new InvalidOperationException("Configure Jwt issuer, audience, and a signing key of at least 64 bytes outside source control.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ClockSkew = TimeSpan.FromSeconds(15),
                NameClaimType = JwtRegisteredClaimNames.Sub,
                RoleClaimType = "role"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var principal = context.Principal!;
                    if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId)
                        || !Guid.TryParse(principal.FindFirstValue("sid"), out var sessionId)
                        || !Guid.TryParse(principal.FindFirstValue("tenant_id"), out var tenantId)
                        || userId == Guid.Empty || sessionId == Guid.Empty || tenantId == Guid.Empty)
                    {
                        context.Fail("Invalid session.");
                        return;
                    }

                    var identity = (ClaimsIdentity)principal.Identity!;
                    foreach (var claim in identity.FindAll("role").ToArray()) identity.RemoveClaim(claim);
                    if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<OrganizationSelectionAuthentication>() is not null)
                    {
                        // The switch validates both proofs under a database lock, including consumed credentials from a revoked source.
                        context.HttpContext.Features.Set(new SignedSession(userId, sessionId, tenantId));
                        return;
                    }

                    var actor = await context.HttpContext.RequestServices.GetRequiredService<ISessionValidator>()
                        .ValidateSessionAsync(userId, sessionId, tenantId, context.HttpContext.RequestAborted);
                    if (actor is null)
                    {
                        context.Fail("Invalid session.");
                        return;
                    }

                    context.HttpContext.RequestServices.GetRequiredService<TenantContext>().SetValidated(actor);
                    identity.AddClaim(new Claim("role", actor.Role.ToString()));
                }
            };
        });

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            options.AddPolicy("OrganizationRead", policy => policy.RequireAuthenticatedUser());
            options.AddPolicy("OrganizationOwner", policy => policy.RequireRole(OrganizationRole.OrganizationOwner.ToString()));
            options.AddPolicy("OperationalRead", policy => policy.RequireAuthenticatedUser());
            options.AddPolicy("OperationalWrite", policy => policy.RequireRole("OrganizationOwner", "Manager"));
            options.AddPolicy("WorkOrderExecute", policy => policy.RequireRole("OrganizationOwner", "Manager", "Technician"));
            options.AddPolicy("OperationalReports", policy => policy.RequireRole("OrganizationOwner", "Manager"));
        });
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-fieldops-csrf";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        services.AddSingleton(_ => PartitionedRateLimiter.Create<HttpContext, (string Ip, bool Auth)>(context =>
        {
            var key = (Ip: context.Connection.RemoteIpAddress?.ToString() ?? "local",
                Auth: context.Request.Path.StartsWithSegments("/api/v1/auth"));
            return RateLimitPartition.GetFixedWindowLimiter(key, partition => new FixedWindowRateLimiterOptions
            {
                PermitLimit = partition.Auth
                    ? configuration.GetValue("RateLimits:AuthPermitLimit", 30)
                    : configuration.GetValue("RateLimits:IpPermitLimit", 240),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) => new ValueTask(RejectRateLimitAsync(context.HttpContext, context.Lease));
            options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "local",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = configuration.GetValue("RateLimits:RequestsPermitLimit", 120),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });
        return services;
    }

    public static async Task LimitBeforeAuthenticationAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Path.StartsWithSegments("/api/v1"))
        {
            var limiter = context.RequestServices.GetRequiredService<PartitionedRateLimiter<HttpContext>>();
            using var lease = limiter.AttemptAcquire(context);
            if (!lease.IsAcquired)
            {
                await RejectRateLimitAsync(context, lease);
                return;
            }
        }

        await next(context);
    }

    private static Task RejectRateLimitAsync(HttpContext context, RateLimitLease lease)
    {
        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ? retry.TotalSeconds : 60;
        context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.Problem(statusCode: 429, title: "Too many requests.",
            extensions: new Dictionary<string, object?> { ["code"] = "rate_limit_exceeded" }).ExecuteAsync(context);
    }

    public static TenantActor Actor(HttpContext context) => context.RequestServices.GetRequiredService<TenantContext>().Actor
        ?? throw new FieldOpsException(ErrorKind.Unauthenticated, "authentication_required", "Authentication is required.");

    internal static SignedSession SelectionSession(HttpContext context) => context.Features.Get<SignedSession>()
        ?? throw new FieldOpsException(ErrorKind.Unauthenticated, "authentication_required", "Authentication is required.");

    public static void NativeRequest(HttpContext context)
    {
        if (context.Request.Headers.ContainsKey("Origin") || context.Request.Headers.ContainsKey("Sec-Fetch-Site")
            || context.Request.Cookies.ContainsKey(RefreshCookie))
            throw new FieldOpsException(ErrorKind.Validation, "native_transport_required", "Use the CSRF-protected web authentication endpoints in a browser.");
    }

    public static async Task WebRequestAsync(HttpContext context)
    {
        RequireWebHttps(context);
        var origins = context.RequestServices.GetRequiredService<IConfiguration>()
            .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        var origin = context.Request.Headers.Origin.ToString();
        if (!origin.StartsWith("https://", StringComparison.Ordinal) || !origins.Contains(origin, StringComparer.Ordinal))
            throw new FieldOpsException(ErrorKind.Forbidden, "origin_rejected", "The request origin is not allowed.");
        try
        {
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            throw new FieldOpsException(ErrorKind.Forbidden, "csrf_rejected", "A valid anti-forgery token is required.");
        }
    }

    public static void RequireWebHttps(HttpContext context)
    {
        if (!context.Request.IsHttps)
            throw new FieldOpsException(ErrorKind.Validation, "https_required", "Web authentication requires HTTPS.");
    }

    public static void SetRefreshCookie(HttpContext context, TokenPair pair)
    {
        context.Response.Cookies.Append(RefreshCookie, pair.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = WebPath,
            Expires = pair.RefreshExpiresAt,
            IsEssential = true
        });
    }

    public static void ClearRefreshCookie(HttpContext context) => context.Response.Cookies.Delete(RefreshCookie,
        new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = WebPath });
}

internal sealed class OrganizationSelectionAuthentication { }
