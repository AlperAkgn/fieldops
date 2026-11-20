using System.Diagnostics;
using System.Text.Json.Serialization;
using FieldOps.Api.Endpoints;
using FieldOps.Api.Collaboration;
using FieldOps.Application.Collaboration;
using FieldOps.Api.Errors;
using FieldOps.Api.Diagnostics;
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
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
});
// The hosting scope includes a raw request path. Request telemetry uses the matched template instead.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Http.Connections", LogLevel.Warning);
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

var useTrustedProxy = DeploymentSecurity.Configure(builder, connectionString, migrate);
builder.Services.AddInfrastructure(connectionString, builder.Configuration);
if (!migrate) builder.Services.AddApiSecurity(builder.Configuration);
if (!migrate) builder.AddFieldOpsTelemetry();
builder.Services.AddSignalR();
builder.Services.AddSingleton<WorkOrderConnections>();
builder.Services.AddScoped<IOrderChangeDelivery, SignalROrderDelivery>();
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

app.Use(ReadinessDeadline.StartAsync);
if (useTrustedProxy) app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.Use(DeploymentSecurity.ApplyResponseHeaders);
app.UseRouting();
app.Use(ApiTelemetry.TrackRequestAsync);

app.UseExceptionHandler();
app.UseStatusCodePages(async context =>
    await Results.Problem(statusCode: context.HttpContext.Response.StatusCode)
        .ExecuteAsync(context.HttpContext));
app.UseCors();
var socketOptions = new WebSocketOptions();
foreach (var origin in origins) socketOptions.AllowedOrigins.Add(origin);
app.UseWebSockets(socketOptions);
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1/live") && context.Request.Headers.Origin.Count != 0 &&
        !origins.Contains(context.Request.Headers.Origin.ToString(), StringComparer.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await next(context);
});
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
app.MapAuditEndpoints();
app.MapReportingEndpoints();
app.MapWorkOrderDetailEndpoints();
app.MapCatalogEndpoints();
app.MapWorkOrderEndpoints();
app.MapSyncEndpoints();
app.MapCollaborationEndpoints();

app.MapGet("/health/live", async (HttpContext context, HealthCheckService healthChecks) =>
{
    var report = await healthChecks.CheckHealthAsync(_ => false, context.RequestAborted);
    await HealthResponseWriter.WriteAsync(context, report);
})
    .WithName("GetLiveness")
    .WithTags("Health")
    .WithSummary("Reports whether the API process can answer requests.")
    .Produces<HealthResponse>().AllowAnonymous();

app.MapGet("/health/ready", ReadinessDeadline.WriteResponseAsync)
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
