using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FieldOps.IntegrationTests;

internal sealed class ApiFactory(string environment = "Development", string? connectionString = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = connectionString
                    ?? "Host=127.0.0.1;Port=1;Database=unavailable;Username=unavailable;Password=secret-test-value;Timeout=1;Pooling=false"
            }));
        return base.CreateHost(builder);
    }
}
