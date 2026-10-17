using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NovaWallet.Infrastructure.Persistence;
using Npgsql;

namespace NovaWallet.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the API in memory against a freshly created and migrated database. Use one per test
/// class (IClassFixture) so classes run in parallel without sharing data.
/// </summary>
public class NovaWalletApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestSigningKey = "integration-tests-signing-key-0123456789abcdef";
    public const string TestIssuer = "novawallet-dev-issuer";
    public const string TestAudience = "novawallet-api";
    private const string HighPermitLimit = "1000000";

    private readonly Dictionary<string, string?> _settings = new()
    {
        ["Auth:SigningKey"] = TestSigningKey,
        ["Auth:Issuer"] = TestIssuer,
        ["Auth:Audience"] = TestAudience,
        ["Auth:DevIssuer:Enabled"] = "true",
        ["RateLimiting:Transfers:PermitLimit"] = HighPermitLimit,
    };

    public string DatabaseName { get; } = $"nw_test_{Guid.NewGuid():N}";

    /// <summary>The app's clock. Starts at a fixed instant; tests may set or advance it.</summary>
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero));

    public string AdminConnectionString { get; private set; } = string.Empty;

    public string RuntimeConnectionString { get; private set; } = string.Empty;

    public TestTokens Tokens { get; } = new(TestSigningKey, TestIssuer, TestAudience);

    public async Task InitializeAsync()
    {
        var server = await PostgresServer.GetAsync();
        await using (var admin = new NpgsqlConnection(server.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE {DatabaseName}";
            await create.ExecuteNonQueryAsync();
        }

        AdminConnectionString = new NpgsqlConnectionStringBuilder(server.AdminConnectionString) { Database = DatabaseName }.ConnectionString;
        RuntimeConnectionString = new NpgsqlConnectionStringBuilder(server.AdminConnectionString)
        {
            Database = DatabaseName,
            Username = PostgresServer.RuntimeRole,
            Password = server.RuntimePassword,
        }.ConnectionString;
        _settings["ConnectionStrings:Ledger"] = RuntimeConnectionString;

        await DatabaseMigrator.MigrateAsync(AdminConnectionString, NullLogger.Instance, CancellationToken.None);
    }

    /// <summary>A factory sharing this database and clock, with extra configuration applied.</summary>
    public WebApplicationFactory<Program> WithSettings(IDictionary<string, string?> overrides) =>
        WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(overrides)));

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        var server = await PostgresServer.GetAsync();
        await using var admin = new NpgsqlConnection(server.AdminConnectionString);
        await admin.OpenAsync();
        await using var drop = admin.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS {DatabaseName} WITH (FORCE)";
        await drop.ExecuteNonQueryAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}
