using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using NovaWallet.IntegrationTests.Infrastructure;
using Npgsql;

namespace NovaWallet.IntegrationTests.RateLimiting;

public class TransferRateLimitTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>, IAsyncLifetime
{
    private const int PermitLimit = 3;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => factory.AssertHoldAsync();

    [Fact]
    public async Task Fourth_transfer_in_the_window_is_429_with_retry_after_and_posts_nothing()
    {
        using var host = Limited();
        var (payer, source, destination) = await ArrangeAsync();
        var client = Client(host, payer);

        for (var i = 0; i < PermitLimit; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await client.TransferAsync(source, destination, 100)).StatusCode);
        }

        var key = Guid.NewGuid().ToString();
        var limited = await client.TransferAsync(source, destination, 100, key);

        await limited.AssertProblemAsync(HttpStatusCode.TooManyRequests, "RATE_LIMITED");
        var retryAfter = int.Parse(Assert.Single(limited.Headers.GetValues("Retry-After")));
        Assert.InRange(retryAfter, 1, 60);
        Assert.Equal(PermitLimit, await ScalarAsync($"SELECT count(*) FROM ledger_transactions WHERE source_wallet_id = '{source}'"));
        Assert.Equal(0, await ScalarAsync($"SELECT count(*) FROM idempotency_records WHERE idempotency_key = '{key}'"));
        await factory.AssertHoldAsync();
    }

    [Fact]
    public async Task Limits_are_per_customer()
    {
        using var host = Limited();
        var (alice, aliceWallet, shared) = await ArrangeAsync();
        var bob = LedgerApi.NewCustomerId();
        var bobWallet = await factory.FundedWalletAsync(bob, 10_000);
        await ExhaustAsync(Client(host, alice), aliceWallet, shared);

        var response = await Client(host, bob).TransferAsync(bobWallet, shared, 100);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Other_endpoints_are_not_limited()
    {
        using var host = Limited();
        var (payer, source, destination) = await ArrangeAsync();
        var client = Client(host, payer);
        await ExhaustAsync(client, source, destination);

        for (var i = 0; i < PermitLimit * 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/wallets/{source}/balance")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/wallets/{source}/statement")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await host.CreateClient().GetAsync("/health/live")).StatusCode);
        }
    }

    [Fact]
    public async Task Unauthenticated_requests_get_401_and_take_no_permit()
    {
        using var host = Limited();
        var (payer, source, destination) = await ArrangeAsync();
        var forged = host.CreateClient().WithBearer(
            new TestTokens(new string('x', 64), NovaWalletApiFactory.TestIssuer, NovaWalletApiFactory.TestAudience).For(payer, TestTokens.WalletScope));

        for (var i = 0; i < PermitLimit * 2; i++)
        {
            await (await forged.TransferAsync(source, destination, 100)).AssertProblemAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        }

        var client = Client(host, payer);
        for (var i = 0; i < PermitLimit; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await client.TransferAsync(source, destination, 100)).StatusCode);
        }
    }

    [Fact]
    public async Task A_valid_token_without_a_subject_is_refused_before_the_limiter()
    {
        using var host = Limited();
        var (_, source, destination) = await ArrangeAsync();
        var anonymousSubject = host.CreateClient().WithBearer(factory.Tokens.Create("ignored", [TestTokens.WalletScope], includeSubject: false));

        for (var i = 0; i < PermitLimit + 2; i++)
        {
            await (await anonymousSubject.TransferAsync(source, destination, 100)).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        }

        await (await anonymousSubject.GetAsync($"/api/v1/wallets/{source}/balance")).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Equal(0, await ScalarAsync($"SELECT count(*) FROM ledger_transactions WHERE source_wallet_id = '{source}'"));
    }

    private WebApplicationFactory<Program> Limited() => factory.WithSettings(new Dictionary<string, string?>
    {
        ["RateLimiting:Transfers:PermitLimit"] = PermitLimit.ToString(),
        ["RateLimiting:Transfers:WindowSeconds"] = "60",
    });

    private HttpClient Client(WebApplicationFactory<Program> host, string customer) =>
        host.CreateClient().WithBearer(factory.Tokens.For(customer, TestTokens.WalletScope));

    private async Task<(string Payer, Guid Source, Guid Destination)> ArrangeAsync()
    {
        var payer = LedgerApi.NewCustomerId();
        var source = await factory.FundedWalletAsync(payer, 10_000);
        var destination = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        return (payer, source, destination);
    }

    private static async Task ExhaustAsync(HttpClient client, Guid source, Guid destination)
    {
        for (var i = 0; i < PermitLimit; i++)
        {
            (await client.TransferAsync(source, destination, 100)).EnsureSuccessStatusCode();
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.TransferAsync(source, destination, 100)).StatusCode);
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
