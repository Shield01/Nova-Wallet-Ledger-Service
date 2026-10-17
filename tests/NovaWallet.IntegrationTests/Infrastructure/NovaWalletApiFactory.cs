using System.Security.Cryptography;
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
/// Hosts the API in memory against a freshly created database, owned and migrated by
/// novawallet_owner and used by the app as novawallet_app, exactly as in docker compose.
/// Use one per test class (IClassFixture) so classes run in parallel without sharing data.
/// </summary>
public class NovaWalletApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestIssuer = "novawallet-dev-issuer";
    public const string TestAudience = "novawallet-api";

    private static readonly string RunSigningKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private readonly Dictionary<string, string?> _settings = new()
    {
        ["Auth:SigningKey"] = RunSigningKey,
        ["Auth:Issuer"] = TestIssuer,
        ["Auth:Audience"] = TestAudience,
        ["Auth:DevIssuer:Enabled"] = "true",
    };

    public string DatabaseName { get; } = $"nw_test_{Guid.NewGuid():N}";

    /// <summary>The app's clock. Starts at a fixed instant; tests may set or advance it.</summary>
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero));

    public string AdminConnectionString { get; private set; } = string.Empty;

    public string OwnerConnectionString { get; private set; } = string.Empty;

    public string RuntimeConnectionString { get; private set; } = string.Empty;

    public TestTokens Tokens { get; } = new(RunSigningKey, TestIssuer, TestAudience);

    public async Task InitializeAsync()
    {
        var server = await PostgresServer.GetAsync();
        await ExecuteAsync(server.AdminConnectionString, $"CREATE DATABASE {DatabaseName} OWNER {PostgresServer.OwnerRole}");

        AdminConnectionString = WithDatabase(server.AdminConnectionString, null, null);
        await ExecuteAsync(AdminConnectionString, $"""
            ALTER SCHEMA public OWNER TO {PostgresServer.OwnerRole};
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            """);

        OwnerConnectionString = WithDatabase(server.AdminConnectionString, PostgresServer.OwnerRole, server.OwnerPassword);
        RuntimeConnectionString = WithDatabase(server.AdminConnectionString, PostgresServer.RuntimeRole, server.RuntimePassword);
        _settings["ConnectionStrings:Ledger"] = RuntimeConnectionString;

        await DatabaseMigrator.MigrateAsync(OwnerConnectionString, NullLogger.Instance, CancellationToken.None);
    }

    /// <summary>A factory sharing this database and clock, with extra configuration applied.</summary>
    public WebApplicationFactory<Program> WithSettings(IDictionary<string, string?> overrides) =>
        WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(overrides)));

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        var server = await PostgresServer.GetAsync();
        await ExecuteAsync(server.AdminConnectionString, $"DROP DATABASE IF EXISTS {DatabaseName} WITH (FORCE)");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddControllers().AddApplicationPart(typeof(TestOnlyController).Assembly);
        });
    }

    private string WithDatabase(string adminConnectionString, string? username, string? password)
    {
        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = DatabaseName };
        if (username is not null)
        {
            builder.Username = username;
            builder.Password = password;
        }

        return builder.ConnectionString;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
