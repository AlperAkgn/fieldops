using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FieldOps.IntegrationTests;

internal sealed class ApiFactory(string environment = "Development", string? connectionString = null,
    IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureTestServices(services =>
        {
            // HTTP tests drive the dispatcher explicitly. Keep the background poller
            // out of tests that deliberately point the API at an unreachable database.
            foreach (var worker in services.Where(service => service.ServiceType == typeof(IHostedService)
                && service.ImplementationType?.Name == "EmailOutboxWorker").ToArray())
            {
                services.Remove(worker);
            }
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var configurationValues = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = connectionString
                ?? "Host=127.0.0.1;Port=1;Database=unavailable;Username=unavailable;Password=secret-test-value;Timeout=1;Pooling=false",
            ["Jwt:Issuer"] = "FieldOps.IntegrationTests",
            ["Jwt:Audience"] = "FieldOps.IntegrationTests.Clients",
            ["Jwt:SigningKey"] = TestSigningKey,
            ["RateLimits:AuthPermitLimit"] = "10000",
            ["RateLimits:IpPermitLimit"] = "10000",
            ["RateLimits:RequestsPermitLimit"] = "10000",
            ["Email:Host"] = "localhost",
            ["Email:Port"] = "1025",
            ["Email:FromAddress"] = "tests@example.test",
            ["Email:UseStartTls"] = "false"
        };
        if (settings is not null)
        {
            foreach (var setting in settings)
            {
                configurationValues[setting.Key] = setting.Value;
            }
        }

        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(configurationValues));
        return base.CreateHost(builder);
    }

    internal const string TestSigningKey =
        "fieldops-tests-only-signing-key-never-use-for-real-credentials-0123456789abcdef";
}
