using System.Diagnostics;
using System.Text.Json.Serialization;
using FieldOps.Api.Endpoints;
using FieldOps.Api.Errors;
using FieldOps.Api.Health;
using FieldOps.Api.Security;
using FieldOps.Infrastructure;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Authorization;

var migrate = args.Contains("--migrate", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(argument => argument != "--migrate").ToArray());

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        };
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, ct) =>
    {
        if (!context.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
            });
        }
        return Task.CompletedTask;
    });
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
});
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16_384);

var connectionString = builder.Configuration.GetConnectionString("Database");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Configure ConnectionStrings__Database using environment variables or .NET user secrets.");
}

builder.Services.AddInfrastructure(connectionString, builder.Configuration);
if (!migrate) builder.Services.AddApiSecurity(builder.Configuration);
var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

if (migrate)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>().Database.MigrateAsync();
    app.Logger.LogInformation("Database migrations applied successfully");
    return;
}

app.Use(async (context, next) =>
{
    var correlationId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        return Task.CompletedTask;
    });
    using var scope = app.Logger.BeginScope(new Dictionary<string, object>
    {
        ["RequestId"] = context.TraceIdentifier,
        ["CorrelationId"] = correlationId
    });

    var startedAt = Stopwatch.GetTimestamp();
    await next(context);
    app.Logger.LogInformation("HTTP {Method} completed {Result} in {Duration} ms",
        context.Request.Method, context.Response.StatusCode, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
});

app.UseExceptionHandler();
app.UseStatusCodePages(async context =>
    await Results.Problem(statusCode: context.HttpContext.Response.StatusCode)
        .ExecuteAsync(context.HttpContext));
app.UseCors();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1"))
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!app.Environment.IsDevelopment() && !context.Request.IsHttps)
        {
            await Results.Problem(statusCode: 400, title: "HTTPS is required.").ExecuteAsync(context);
            return;
        }
    }
    await next(context);
});
app.Use(ApiSecurity.LimitBeforeAuthenticationAsync);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapAuthEndpoints();
app.MapOrganizationEndpoints();

app.MapGet("/health/live", async (HttpContext context, HealthCheckService healthChecks) =>
{
    var report = await healthChecks.CheckHealthAsync(_ => false, context.RequestAborted);
    await HealthResponseWriter.WriteAsync(context, report);
})
    .WithName("GetLiveness")
    .WithTags("Health")
    .WithSummary("Reports whether the API process can answer requests.")
    .Produces<HealthResponse>().AllowAnonymous();

app.MapGet("/health/ready", async (HttpContext context, HealthCheckService healthChecks) =>
{
    var report = await healthChecks.CheckHealthAsync(check => check.Tags.Contains("ready"), context.RequestAborted);
    await HealthResponseWriter.WriteAsync(context, report);
})
    .WithName("GetReadiness")
    .WithTags("Health")
    .WithSummary("Checks PostgreSQL connectivity and that all migrations are applied.")
    .Produces<HealthResponse>()
    .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable).AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapFallback("/{**path}", () => Results.NotFound()).AllowAnonymous().ExcludeFromDescription();

await app.RunAsync();

// Exposed for the ASP.NET Core integration-test host.
public partial class Program;
