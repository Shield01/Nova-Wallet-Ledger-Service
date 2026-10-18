using System.Net;
using NovaWallet.IntegrationTests.Infrastructure;
using Npgsql;

namespace NovaWallet.IntegrationTests.Fixture;

public class FixtureSelfTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    [Fact]
    public async Task Hosted_api_answers_liveness()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Test_database_is_migrated_before_the_first_test()
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM __ef_migrations_history", connection);

        var applied = (long)(await command.ExecuteScalarAsync())!;

        Assert.True(applied > 0);
    }

    [Fact]
    public async Task Test_server_and_pools_leave_room_for_parallel_test_classes()
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SHOW max_connections", connection);
        var maxConnections = int.Parse((string)(await command.ExecuteScalarAsync())!);

        Assert.Equal(NovaWalletApiFactory.MaxRuntimePoolSize, new NpgsqlConnectionStringBuilder(factory.RuntimeConnectionString).MaxPoolSize);
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresServer.ExternalServerVariable)))
        {
            // Only when the fixture started the server; an external server is configured by its owner.
            Assert.Equal(PostgresServer.MaxConnections, maxConnections);
        }
    }

    [Fact]
    public async Task Runtime_role_can_connect_to_the_test_database()
    {
        await using var connection = new NpgsqlConnection(factory.RuntimeConnectionString);
        await connection.OpenAsync();

        Assert.Equal("novawallet_app", connection.UserName);
    }

    [Fact]
    public async Task Each_factory_gets_its_own_database()
    {
        await using var other = new NovaWalletApiFactory();
        await other.InitializeAsync();
        try
        {
            var customerId = $"isolation-{Guid.NewGuid():N}"[..30];
            await factory.CreateWalletAsync(customerId);

            await using var connection = new NpgsqlConnection(other.OwnerConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT count(*) FROM wallets WHERE customer_id = @c", connection);
            command.Parameters.AddWithValue("c", customerId);
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
        finally
        {
            await other.DisposeAsync();
        }
    }

    [Fact]
    public void Clock_is_controllable()
    {
        var before = factory.Clock.GetUtcNow();
        factory.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(before.AddHours(1), factory.Clock.GetUtcNow());
    }

    [Fact]
    public void Settings_can_be_overridden_per_test()
    {
        using var overridden = factory.WithSettings(new Dictionary<string, string?> { ["Custom:Value"] = "42" });
        var configuration = (Microsoft.Extensions.Configuration.IConfiguration)overridden.Services.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration))!;

        Assert.Equal("42", configuration["Custom:Value"]);
    }
}
