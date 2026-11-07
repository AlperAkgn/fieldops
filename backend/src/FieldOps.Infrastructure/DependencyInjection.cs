using System.Text;
using FieldOps.Application.Identity;
using FieldOps.Application.Catalog;
using FieldOps.Application.WorkOrders;
using FieldOps.Application.Operations;
using FieldOps.Application.Organizations;
using FieldOps.Application.Tenancy;
using FieldOps.Application.WorkOrderDetails;
using FieldOps.Infrastructure.Authentication;
using FieldOps.Infrastructure.Email;
using FieldOps.Infrastructure.Health;
using FieldOps.Infrastructure.Identity;
using FieldOps.Infrastructure.Catalog;
using FieldOps.Infrastructure.WorkOrders;
using FieldOps.Infrastructure.Operations;
using FieldOps.Infrastructure.Organizations;
using FieldOps.Infrastructure.Persistence;
using FieldOps.Infrastructure.WorkOrderDetails;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FieldOps.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString, IConfiguration? configuration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<FieldOpsDbContext>(options =>
            options.UseNpgsql(connectionString, database => database.CommandTimeout(5)));
        services.AddHealthChecks().AddCheck<PostgreSqlReadinessCheck>(
            "postgresql", tags: ["ready"], timeout: TimeSpan.FromSeconds(5));
        services.TryAddScoped<TenantContext>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<OperationalWriteContext>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IWorkOrderService, WorkOrderService>();
        services.AddScoped<IAuditLogReader, AuditLogReader>();
        services.AddScoped<IChecklistTemplateService, ChecklistTemplateService>();
        services.AddScoped<IWorkOrderDetailService, WorkOrderDetailService>();
        if (configuration is not null) services.AddIdentityInfrastructure(configuration);

        return services;
    }

    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var protection = services.AddDataProtection().SetApplicationName("FieldOps");
        var keyPath = configuration["DataProtection:KeyPath"];
        if (!string.IsNullOrWhiteSpace(keyPath))
        {
            Directory.CreateDirectory(keyPath);
            protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        }
        services.AddIdentityCore<FieldOpsUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<FieldOpsDbContext>().AddDefaultTokenProviders();
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection("Jwt"))
            .Validate(options => Encoding.UTF8.GetByteCount(options.SigningKey) >= 64, "JWT signing key must contain at least 64 UTF-8 bytes.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer) && !string.IsNullOrWhiteSpace(options.Audience), "JWT issuer and audience are required.")
            .Validate(options => options.AccessTokenMinutes is >= 1 and <= 10 && options.RefreshTokenDays is >= 1 and <= 30 &&
                options.AbsoluteSessionDays >= options.RefreshTokenDays && options.AbsoluteSessionDays <= 90, "Invalid session lifetime configuration.")
            .ValidateOnStart();
        services.AddOptions<SmtpOptions>().Bind(configuration.GetSection("Email"))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Host) && options.Port is > 0 and <= 65535 &&
                !string.IsNullOrWhiteSpace(options.FromAddress), "SMTP configuration is invalid.")
            .Validate(options => string.IsNullOrWhiteSpace(options.UserName) == string.IsNullOrWhiteSpace(options.Password),
                "SMTP username and password must either both be configured or both be absent.").ValidateOnStart();
        services.AddScoped<AccessTokenIssuer>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<ISessionValidator, SessionValidator>();
        services.AddScoped<IEmailOutbox, EmailOutbox>();
        services.AddScoped<IEmailDelivery, SmtpEmailDelivery>();
        services.AddScoped<IEmailDispatcher, EmailDispatcher>();
        services.AddHostedService<EmailOutboxWorker>();
        return services;
    }
}
