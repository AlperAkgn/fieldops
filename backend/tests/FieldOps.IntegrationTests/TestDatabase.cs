using Npgsql;

namespace FieldOps.IntegrationTests;

// Each test owns a uniquely named database. Never recreate the configured database.
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string adminConnectionString;
    private readonly string databaseName;

    private TestDatabase(string adminConnectionString, string databaseName)
    {
        this.adminConnectionString = adminConnectionString;
        this.databaseName = databaseName;
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = databaseName,
            Pooling = false
        }.ConnectionString;
    }

    public string ConnectionString { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        var configured = Environment.GetEnvironmentVariable("FIELDOPS_TEST_DATABASE");
        Assert.False(string.IsNullOrWhiteSpace(configured),
            "Set FIELDOPS_TEST_DATABASE to a disposable PostgreSQL administrative connection with CREATE DATABASE " +
            "permission. Use --filter Category!=Database for explicitly database-free verification.");

        var adminSettings = new NpgsqlConnectionStringBuilder(configured) { Pooling = false };
        var databaseName = $"fieldops_test_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(adminSettings.ConnectionString);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
        await create.ExecuteNonQueryAsync();
        return new TestDatabase(adminSettings.ConnectionString, databaseName);
    }

    public async ValueTask DisposeAsync()
    {
        // The name is generated internally above, never supplied by an HTTP caller or test environment.
        if (!databaseName.StartsWith("fieldops_test_", StringComparison.Ordinal)
            || databaseName.Length != "fieldops_test_".Length + 32)
        {
            throw new InvalidOperationException("Refusing to drop a database outside the test namespace.");
        }

        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
