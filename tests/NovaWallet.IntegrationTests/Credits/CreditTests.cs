using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NovaWallet.Application.Audit;
using NovaWallet.Infrastructure.Persistence;
using NovaWallet.IntegrationTests.Infrastructure;
using Npgsql;

namespace NovaWallet.IntegrationTests.Credits;

public class CreditTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => factory.AssertHoldAsync();

    [Fact]
    public async Task Credit_raises_the_balance_with_a_balanced_double_entry_and_two_audit_records()
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        var systemBefore = await BalanceAsync(Guid.Parse(LedgerApiMoney.SystemAccountId));

        var response = await factory.InboundSystemClient().CreditAsync(walletId, 500_000, correlationId: "corr-credit-1");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var transactionId = body.GetProperty("transactionId").GetGuid();
        Assert.Equal(walletId, body.GetProperty("walletId").GetGuid());
        Assert.Equal("CREDIT", body.GetProperty("type").GetString());
        Assert.Equal(500_000, body.GetProperty("amountKobo").GetInt64());
        Assert.Equal(500_000, body.GetProperty("balanceAfterKobo").GetInt64());
        var createdAt = body.GetProperty("createdAt").GetDateTimeOffset();

        Assert.Equal(500_000, await BalanceAsync(walletId));
        Assert.Equal(systemBefore - 500_000, await BalanceAsync(Guid.Parse(LedgerApiMoney.SystemAccountId)));

        var entries = await QueryAsync($"SELECT entry_type, amount_kobo, balance_after_kobo, created_at FROM ledger_entries WHERE transaction_id = '{transactionId}' ORDER BY amount_kobo");
        Assert.Equal(2, entries.Count);
        Assert.Equal(("CREDIT_SETTLEMENT", -500_000L), ((string)entries[0][0], (long)entries[0][1]));
        Assert.Equal(("CREDIT", 500_000L, 500_000L), ((string)entries[1][0], (long)entries[1][1], (long)entries[1][2]));
        Assert.All(entries, e => Assert.Equal(createdAt, new DateTimeOffset((DateTime)e[3])));

        var audit = await QueryAsync($"SELECT action, wallet_id, amount_kobo, balance_before_kobo, balance_after_kobo, actor, correlation_id, occurred_at FROM audit_log WHERE transaction_id = '{transactionId}' ORDER BY amount_kobo");
        Assert.Equal(2, audit.Count);
        Assert.Equal("SYSTEM_DEBIT", audit[0][0]);
        Assert.Equal(("CREDIT", walletId, 500_000L, 0L, 500_000L), ((string)audit[1][0], (Guid)audit[1][1], (long)audit[1][2], (long)audit[1][3], (long)audit[1][4]));
        Assert.All(audit, a => Assert.Equal(("nip-gateway", "corr-credit-1"), ((string)a[5], (string)a[6])));
        Assert.All(audit, a => Assert.Equal(createdAt, new DateTimeOffset((DateTime)a[7])));
    }

    [Fact]
    public async Task Replay_with_the_same_key_returns_the_original_and_moves_money_once()
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        var client = factory.InboundSystemClient();
        var key = Guid.NewGuid().ToString();
        var first = await client.CreditAsync(walletId, 250_000, key);

        var replay = await client.CreditAsync(walletId, 250_000, key);

        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotent-Replayed")));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(250_000, await BalanceAsync(walletId));
        Assert.Equal(1L, await CountAsync($"SELECT count(*) FROM ledger_transactions WHERE destination_wallet_id = '{walletId}'"));
    }

    [Fact]
    public async Task Same_key_for_a_different_amount_or_wallet_is_refused_and_posts_nothing()
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        var otherWallet = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        var client = factory.InboundSystemClient();
        var key = Guid.NewGuid().ToString();
        await client.CreditAsync(walletId, 1_000, key);

        var differentAmount = await client.CreditAsync(walletId, 1_001, key);
        var differentWallet = await client.CreditAsync(otherWallet, 1_000, key);

        await differentAmount.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "IDEMPOTENCY_KEY_REUSED");
        await differentWallet.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "IDEMPOTENCY_KEY_REUSED");
        Assert.Equal(1_000, await BalanceAsync(walletId));
        Assert.Equal(0, await BalanceAsync(otherWallet));
    }

    [Fact]
    public async Task Twenty_concurrent_requests_with_one_key_post_exactly_once()
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        var client = factory.InboundSystemClient();
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.CreditAsync(walletId, 10_000, key)));

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict, r.StatusCode.ToString()));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(10_000, await BalanceAsync(walletId));
        Assert.Equal(1L, await CountAsync($"SELECT count(*) FROM ledger_transactions WHERE destination_wallet_id = '{walletId}'"));
    }

    [Fact]
    public async Task Concurrent_credits_with_different_keys_all_land()
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        var client = factory.InboundSystemClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => client.CreditAsync(walletId, 1_000)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(25_000, await BalanceAsync(walletId));
    }

    [Theory]
    [InlineData(null, "IDEMPOTENCY_KEY_REQUIRED")]
    [InlineData("bad key", "IDEMPOTENCY_KEY_INVALID")]
    public async Task Missing_or_invalid_key_is_rejected(string? key, string code)
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/wallets/{walletId}/credits") { Content = JsonContent.Create(new { amountKobo = 100 }) };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        var response = await factory.InboundSystemClient().SendAsync(request);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, code);
        Assert.Equal(0, await BalanceAsync(walletId));
    }

    [Theory]
    [InlineData("""{"amountKobo":0}""")]
    [InlineData("""{"amountKobo":-5}""")]
    [InlineData("""{"amountKobo":10.5}""")]
    [InlineData("""{"amountKobo":"100"}""")]
    [InlineData("""{}""")]
    [InlineData("""{"amountKobo":9007199254740992}""")]
    public async Task Invalid_amounts_are_rejected_and_nothing_is_posted(string json)
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/wallets/{walletId}/credits") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await factory.InboundSystemClient().SendAsync(request);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        Assert.Equal(0, await BalanceAsync(walletId));
    }

    [Fact]
    public async Task Largest_allowed_amount_is_accepted()
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());

        var response = await factory.InboundSystemClient().CreditAsync(walletId, 9_007_199_254_740_991);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("8f14e45f-ceea-4e67-a8f0-6d2a3c1b9e01")]
    [InlineData(LedgerApiMoney.SystemAccountId)]
    public async Task Unknown_wallet_or_the_settlement_account_is_not_found(string walletId)
    {
        var response = await factory.InboundSystemClient().CreditAsync(Guid.Parse(walletId), 100);

        await response.AssertProblemAsync(HttpStatusCode.NotFound, "WALLET_NOT_FOUND");
    }

    [Fact]
    public async Task Customers_cannot_credit_even_their_own_wallet()
    {
        var customerId = LedgerApi.NewCustomerId();
        var walletId = await factory.CreateWalletAsync(customerId);

        var customer = await factory.CustomerClient(customerId).CreditAsync(walletId, 100);
        var anonymous = await factory.CreateClient().CreditAsync(walletId, 100);

        await customer.AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await anonymous.AssertProblemAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        Assert.Equal(0, await BalanceAsync(walletId));
    }

    [Fact]
    public async Task Failure_after_entries_are_written_rolls_everything_back_and_the_key_can_be_retried()
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        var key = Guid.NewGuid().ToString();
        using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IAuditTrail>(sp => new FailingAuditTrail(sp.GetRequiredService<LedgerDbContext>()))));

        var failed = await failing.CreateClient().WithBearer(factory.Tokens.For("nip-gateway", TestTokens.CreditScope)).CreditAsync(walletId, 7_000, key);

        await failed.AssertProblemAsync(HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        Assert.Equal(0, await BalanceAsync(walletId));
        Assert.Equal(0L, await CountAsync($"SELECT count(*) FROM ledger_entries WHERE wallet_id = '{walletId}'"));
        Assert.Equal(0L, await CountAsync($"SELECT count(*) FROM audit_log WHERE wallet_id = '{walletId}'"));
        Assert.Equal(0L, await CountAsync($"SELECT count(*) FROM idempotency_records WHERE idempotency_key = '{key}'"));

        var retry = await factory.InboundSystemClient().CreditAsync(walletId, 7_000, key);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(7_000, await BalanceAsync(walletId));
    }

    [Fact]
    public async Task Lock_wait_beyond_the_timeout_returns_503_and_posts_nothing()
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        await using var holder = new NpgsqlConnection(factory.OwnerConnectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using (var lockRow = new NpgsqlCommand($"SELECT 1 FROM wallets WHERE id = '{walletId}' FOR UPDATE", holder, transaction))
        {
            await lockRow.ExecuteNonQueryAsync();
        }

        using var impatient = factory.WithSettings(new Dictionary<string, string?> { ["Database:LockTimeoutMilliseconds"] = "300" });
        var response = await impatient.CreateClient().WithBearer(factory.Tokens.For("nip-gateway", TestTokens.CreditScope)).CreditAsync(walletId, 100);
        await transaction.RollbackAsync();

        await response.AssertProblemAsync(HttpStatusCode.ServiceUnavailable, "SERVICE_UNAVAILABLE");
        Assert.Equal(0, await BalanceAsync(walletId));
    }

    [Fact]
    public async Task Runtime_role_can_only_read_and_append_to_the_ledger()
    {
        var grants = await QueryAsync(
            "SELECT table_name::text, string_agg(privilege_type, ',' ORDER BY privilege_type) FROM information_schema.role_table_grants WHERE grantee = 'novawallet_app' AND table_name IN ('ledger_transactions', 'ledger_entries') GROUP BY table_name ORDER BY table_name");

        Assert.Equal(2, grants.Count);
        Assert.All(grants, g => Assert.Equal("INSERT,SELECT", g[1]));
    }

    private async Task<long> BalanceAsync(Guid walletId) =>
        await CountAsync($"SELECT balance_kobo FROM wallets WHERE id = '{walletId}'");

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task<List<object[]>> QueryAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object[]>();
        while (await reader.ReadAsync())
        {
            var row = new object[reader.FieldCount];
            reader.GetValues(row);
            rows.Add(row);
        }

        return rows;
    }
}
