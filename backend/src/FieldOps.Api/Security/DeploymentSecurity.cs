using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;

namespace FieldOps.Api.Security;

internal static class DeploymentSecurity
{
    public static bool Configure(WebApplicationBuilder builder, string connectionString, bool migrate)
    {
        var settings = builder.Configuration;
        var development = builder.Environment.IsDevelopment();
        if (settings.GetValue<bool>("FORWARDEDHEADERS_ENABLED") ||
            bool.TryParse(settings["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], out var automatic) && automatic)
            throw new InvalidOperationException("Use Proxy:KnownProxies to configure trusted forwarding.");

        var origins = settings.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            !string.Equals(origin, uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase) ||
            uri.UserInfo.Length != 0 || uri.Scheme != "https" && (!development || uri.Scheme != "http")))
            throw new InvalidOperationException("Configure explicit HTTP(S) origins with HTTPS required outside development.");

        if (!development)
        {
            if (new NpgsqlConnectionStringBuilder(connectionString).SslMode != SslMode.VerifyFull)
                throw new InvalidOperationException("Configure PostgreSQL with SSL Mode=VerifyFull outside development.");
            if (!migrate) ValidateHostedSettings(settings);
        }

        var proxies = settings.GetSection("Proxy:KnownProxies").Get<string[]>() ?? [];
        if (proxies.Length == 0 || migrate) return false;
        var addresses = proxies.Select(value => IPAddress.TryParse(value, out var address) &&
                !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any)
            ? address : throw new InvalidOperationException("Proxy:KnownProxies requires exact proxy IP addresses.")).ToArray();
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var address in addresses) options.KnownProxies.Add(address);
        });
        return true;
    }

    private static void ValidateHostedSettings(IConfiguration settings)
    {
        var hosts = settings["AllowedHosts"]?.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (hosts.Length == 0 || hosts.Any(host => Uri.CheckHostName(host) == UriHostNameType.Unknown))
            throw new InvalidOperationException("Configure explicit AllowedHosts outside development.");
        if (string.IsNullOrWhiteSpace(settings["DataProtection:KeyPath"]) || string.IsNullOrWhiteSpace(settings["Email:Host"]) ||
            !settings.GetValue("Email:UseStartTls", true))
            throw new InvalidOperationException("Configure a persistent data-protection key ring and TLS SMTP outside development.");

        var endpoint = settings["ObjectStorage:Endpoint"];
        var access = settings["ObjectStorage:AccessKey"];
        var secret = settings["ObjectStorage:SecretKey"];
        if (settings.GetValue<bool>("ObjectStorage:CreateLocalBucket") ||
            (endpoint is not null || access is not null || secret is not null) &&
            (!Uri.TryCreate(endpoint, UriKind.Absolute, out var storage) || storage.Scheme != "https" ||
                storage.UserInfo.Length != 0 || storage.Query.Length != 0 || storage.Fragment.Length != 0 ||
                string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(secret) ||
                string.IsNullOrWhiteSpace(settings["ObjectStorage:Bucket"])))
            throw new InvalidOperationException("Configure HTTPS and a private, provisioned object-storage bucket outside development.");
    }

    public static Task ApplyResponseHeaders(HttpContext context, RequestDelegate next)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
            return Task.CompletedTask;
        });
        return next(context);
    }
}
