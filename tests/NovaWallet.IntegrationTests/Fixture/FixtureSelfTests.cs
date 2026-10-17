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
            Assert.NotEqual(factory.DatabaseName, other.DatabaseName);
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
