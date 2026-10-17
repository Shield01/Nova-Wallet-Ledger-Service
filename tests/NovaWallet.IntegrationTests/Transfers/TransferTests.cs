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

namespace NovaWallet.IntegrationTests.Transfers;

public class TransferTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => factory.AssertHoldAsync();

    [Fact]
    public async Task Transfer_moves_money_atomically_with_balanced_entries_and_audit()
    {
        var (a, aId) = await CustomerAsync(1_000_000);
        var (_, bId) = await CustomerAsync(0);

        var response = await a.TransferAsync(aId, bId, 300_000);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var transferId = body.GetProperty("transferId").GetGuid();
        Assert.Equal(aId, body.GetProperty("sourceWalletId").GetGuid());
        Assert.Equal(bId, body.GetProperty("destinationWalletId").GetGuid());
        Assert.Equal(300_000, body.GetProperty("amountKobo").GetInt64());
        Assert.Equal(700_000, body.GetProperty("sourceBalanceAfterKobo").GetInt64());
        var createdAt = body.GetProperty("createdAt").GetDateTimeOffset();
        Assert.Equal(700_000, await BalanceAsync(aId));
        Assert.Equal(300_000, await BalanceAsync(bId));

        var entries = await QueryAsync($"SELECT entry_type, amount_kobo, wallet_id, counterparty_wallet_id, created_at FROM ledger_entries WHERE transaction_id = '{transferId}' ORDER BY amount_kobo");
        Assert.Equal(("TRANSFER_OUT", -300_000L, aId, bId), ((string)entries[0][0], (long)entries[0][1], (Guid)entries[0][2], (Guid)entries[0][3]));
        Assert.Equal(("TRANSFER_IN", 300_000L, bId, aId), ((string)entries[1][0], (long)entries[1][1], (Guid)entries[1][2], (Guid)entries[1][3]));
        Assert.All(entries, e => Assert.Equal(createdAt, new DateTimeOffset((DateTime)e[4])));

        var audit = await QueryAsync($"SELECT action, balance_before_kobo, balance_after_kobo, occurred_at FROM audit_log WHERE transaction_id = '{transferId}' ORDER BY action");
        Assert.Equal(("TRANSFER_CREDIT", 0L, 300_000L), ((string)audit[0][0], (long)audit[0][1], (long)audit[0][2]));
        Assert.Equal(("TRANSFER_DEBIT", 1_000_000L, 700_000L), ((string)audit[1][0], (long)audit[1][1], (long)audit[1][2]));
        Assert.All(audit, r => Assert.Equal(createdAt, new DateTimeOffset((DateTime)r[3])));
    }

    [Fact]
    public async Task Overdraft_is_refused_and_nothing_moves()
    {
        var (a, aId) = await CustomerAsync(100);
        var (_, bId) = await CustomerAsync(0);

        var response = await a.TransferAsync(aId, bId, 101);

        await response.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "INSUFFICIENT_FUNDS");
        Assert.Equal(100, await BalanceAsync(aId));
        Assert.Equal(0, await BalanceAsync(bId));
    }

    [Fact]
    public async Task Sending_the_whole_balance_is_allowed_and_leaves_zero()
    {
        var (a, aId) = await CustomerAsync(5_000);
        var (_, bId) = await CustomerAsync(0);

        var response = await a.TransferAsync(aId, bId, 5_000);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(0, await BalanceAsync(aId));
    }

    [Fact]
    public async Task Transfer_to_the_same_wallet_is_refused()
    {
        var (a, aId) = await CustomerAsync(5_000);

        var response = await a.TransferAsync(aId, aId, 100);

        await response.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "SAME_WALLET_TRANSFER");
    }

    [Theory]
    [InlineData("8f14e45f-ceea-4e67-a8f0-6d2a3c1b9e01")]
    [InlineData(LedgerApiMoney.SystemAccountId)]
    public async Task Unknown_destination_or_the_settlement_account_is_refused(string destination)
    {
        var (a, aId) = await CustomerAsync(5_000);

        var response = await a.TransferAsync(aId, Guid.Parse(destination), 100);

        await response.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "DESTINATION_WALLET_NOT_FOUND");
        Assert.Equal(5_000, await BalanceAsync(aId));
    }

    [Fact]
    public async Task Sending_from_a_wallet_you_do_not_own_is_not_found()
    {
        var (_, victimId) = await CustomerAsync(5_000);
        var (thief, thiefId) = await CustomerAsync(0);

        var stranger = await thief.TransferAsync(victimId, thiefId, 100);
        var unknown = await thief.TransferAsync(Guid.NewGuid(), thiefId, 100);

        await stranger.AssertProblemAsync(HttpStatusCode.NotFound, "WALLET_NOT_FOUND");
        await unknown.AssertProblemAsync(HttpStatusCode.NotFound, "WALLET_NOT_FOUND");
        Assert.Equal(5_000, await BalanceAsync(victimId));
    }

    [Fact]
    public async Task Refusal_precedence_follows_the_architecture()
    {
        var (a, aId) = await CustomerAsync(10);
        var (_, strangerWallet) = await CustomerAsync(0);

        // Not-owned source beats everything else; same wallet beats unknown destination and funds.
        var notOwnedAndSame = await a.TransferAsync(strangerWallet, strangerWallet, 1_000);
        var sameAndPoor = await a.TransferAsync(aId, aId, 1_000);
        var unknownDestinationAndPoor = await a.TransferAsync(aId, Guid.NewGuid(), 1_000);

        await notOwnedAndSame.AssertProblemAsync(HttpStatusCode.NotFound, "WALLET_NOT_FOUND");
        await sameAndPoor.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "SAME_WALLET_TRANSFER");
        await unknownDestinationAndPoor.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "DESTINATION_WALLET_NOT_FOUND");
    }

    [Fact]
    public async Task Idempotency_replays_successes_and_refusals_and_rejects_reused_keys()
    {
        var (a, aId) = await CustomerAsync(1_000);
        var (_, bId) = await CustomerAsync(0);
        var ok = Guid.NewGuid().ToString();
        var refused = Guid.NewGuid().ToString();

        var first = await a.TransferAsync(aId, bId, 400, ok);
        var replay = await a.TransferAsync(aId, bId, 400, ok);
        var reused = await a.TransferAsync(aId, bId, 401, ok);
        var poor = await a.TransferAsync(aId, bId, 10_000, refused);
        await factory.InboundSystemClient().CreditAsync(aId, 100_000);
        var poorReplay = await a.TransferAsync(aId, bId, 10_000, refused);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.True(replay.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        await reused.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "IDEMPOTENCY_KEY_REUSED");
        await poor.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "INSUFFICIENT_FUNDS");
        await poorReplay.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "INSUFFICIENT_FUNDS");
        Assert.True(poorReplay.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(400, await BalanceAsync(bId));
    }

    [Fact]
    public async Task Concurrent_duplicate_requests_post_once()
    {
        var (a, aId) = await CustomerAsync(10_000);
        var (_, bId) = await CustomerAsync(0);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => a.TransferAsync(aId, bId, 1_000, key)));

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict, r.StatusCode.ToString()));
        Assert.Equal(1_000, await BalanceAsync(bId));
    }

    [Fact]
    public async Task Missing_key_is_rejected()
    {
        var (a, aId) = await CustomerAsync(1_000);
        var (_, bId) = await CustomerAsync(0);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transfers") { Content = JsonContent.Create(new { sourceWalletId = aId, destinationWalletId = bId, amountKobo = 1 }) };

        var response = await a.SendAsync(request);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "IDEMPOTENCY_KEY_REQUIRED");
    }

    [Theory]
    [InlineData("""{"sourceWalletId":"{A}","destinationWalletId":"{B}","amountKobo":0}""")]
    [InlineData("""{"sourceWalletId":"{A}","destinationWalletId":"{B}","amountKobo":12.5}""")]
    [InlineData("""{"sourceWalletId":"{A}","destinationWalletId":"{B}","amountKobo":"100"}""")]
    [InlineData("""{"sourceWalletId":"not-a-guid","destinationWalletId":"{B}","amountKobo":100}""")]
    [InlineData("""{"destinationWalletId":"{B}","amountKobo":100}""")]
    [InlineData("""{"sourceWalletId":"{A}","destinationWalletId":"{B}","amountKobo":9007199254740992}""")]
    public async Task Invalid_requests_are_rejected(string template)
    {
        var (a, aId) = await CustomerAsync(1_000);
        var (_, bId) = await CustomerAsync(0);
        var json = template.Replace("{A}", aId.ToString()).Replace("{B}", bId.ToString());
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transfers") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await a.SendAsync(request);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        Assert.Equal(1_000, await BalanceAsync(aId));
    }

    [Fact]
    public async Task Fifty_concurrent_transfers_against_a_balance_for_twenty_never_overdraw()
    {
        var (a, aId) = await CustomerAsync(1_000_000);
        var (_, bId) = await CustomerAsync(0);

        var responses = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => a.TransferAsync(aId, bId, 50_000)));

        Assert.Equal(20, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(30, responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        Assert.Equal(0, await BalanceAsync(aId));
        Assert.Equal(1_000_000, await BalanceAsync(bId));
    }

    [Fact]
    public async Task Opposite_direction_transfers_at_once_do_not_deadlock()
    {
        var (a, aId) = await CustomerAsync(100_000);
        var (b, bId) = await CustomerAsync(100_000);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .SelectMany(_ => new[] { a.TransferAsync(aId, bId, 1_000), b.TransferAsync(bId, aId, 1_000) }));

        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(200_000, await BalanceAsync(aId) + await BalanceAsync(bId));
    }

    [Fact]
    public async Task Failure_after_entries_are_written_leaves_nothing_behind()
    {
        var customerId = LedgerApi.NewCustomerId();
        var aId = await factory.FundedWalletAsync(customerId, 9_000);
        var (_, bId) = await CustomerAsync(0);
        var key = Guid.NewGuid().ToString();
        using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IAuditTrail>(sp => new FailingAuditTrail(sp.GetRequiredService<LedgerDbContext>()))));

        var response = await failing.CreateClient().WithBearer(factory.Tokens.For(customerId, TestTokens.WalletScope)).TransferAsync(aId, bId, 4_000, key);

        await response.AssertProblemAsync(HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        Assert.Equal(9_000, await BalanceAsync(aId));
        Assert.Equal(0, await BalanceAsync(bId));
        Assert.Equal(0L, await ScalarAsync($"SELECT count(*) FROM ledger_entries WHERE wallet_id = '{bId}'"));
        Assert.Equal(0L, await ScalarAsync($"SELECT count(*) FROM idempotency_records WHERE idempotency_key = '{key}'"));
    }

    [Fact]
    public async Task Missing_token_and_missing_scope_are_refused()
    {
        var (_, aId) = await CustomerAsync(1_000);
        var (_, bId) = await CustomerAsync(0);

        var anonymous = await factory.CreateClient().TransferAsync(aId, bId, 1);
        var creditSystem = await factory.InboundSystemClient().TransferAsync(aId, bId, 1);

        await anonymous.AssertProblemAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await creditSystem.AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    private async Task<(HttpClient Client, Guid WalletId)> CustomerAsync(long fundsKobo)
    {
        var customerId = LedgerApi.NewCustomerId();
        var walletId = await factory.FundedWalletAsync(customerId, fundsKobo);
        return (factory.CustomerClient(customerId), walletId);
    }

    private Task<long> BalanceAsync(Guid walletId) => ScalarAsync($"SELECT balance_kobo FROM wallets WHERE id = '{walletId}'");

    private async Task<long> ScalarAsync(string sql)
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
