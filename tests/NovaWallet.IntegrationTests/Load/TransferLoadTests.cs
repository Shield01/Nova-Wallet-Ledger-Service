using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NovaWallet.Application.Ledger;
using NovaWallet.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit.Abstractions;

namespace NovaWallet.IntegrationTests.Load;

/// <summary>
/// Mixed, seeded transfer load across ten wallets, then every money rule checked in SQL. Three
/// "rich" wallets trade large amounts among themselves until the daily limit stops them; seven
/// "poor" wallets trade small amounts among themselves until they run out of money.
/// </summary>
[Trait("Category", "Load")]
public class TransferLoadTests(NovaWalletApiFactory factory, ITestOutputHelper output) : IClassFixture<NovaWalletApiFactory>
{
    private const int Seed = 20261017;
    private const int Requests = 500;
    private const int Parallelism = 32;
    private const int RichWallets = 3;
    private const int Wallets = 10;

    [Fact]
    public async Task Constraints_hold_under_mixed_concurrent_transfers()
    {
        // The configured limit, not a copy of the default, so the check follows any change to it.
        var dailyLimitKobo = factory.Services.GetRequiredService<IOptions<LimitSettings>>().Value.DailyOutboundKobo;
        output.WriteLine($"seed={Seed} requests={Requests} parallelism={Parallelism} dailyLimitKobo={dailyLimitKobo}");
        var random = new Random(Seed);
        var customers = new List<(string Customer, Guid Wallet)>();
        for (var i = 0; i < Wallets; i++)
        {
            var customer = $"load-{i}-{Guid.NewGuid():N}"[..24];
            customers.Add((customer, await factory.FundedWalletAsync(customer, i < RichWallets ? dailyLimitKobo * 4 : dailyLimitKobo / 50)));
        }

        var plan = Plan(random, customers, dailyLimitKobo);
        var results = new ConcurrentBag<Outcome>();
        await Parallel.ForEachAsync(plan, new ParallelOptions { MaxDegreeOfParallelism = Parallelism }, async (request, _) =>
        {
            var client = factory.CustomerClient(request.Customer);
            var response = await client.TransferAsync(request.Source, request.Destination, request.AmountKobo, request.Key);
            var code = (int)response.StatusCode >= 400
                ? (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
                : null;
            results.Add(new Outcome(request.Key, response.StatusCode, code, response.Headers.Contains("Idempotent-Replayed")));
        });

        var outcomes = results.ToList();
        output.WriteLine(string.Join(", ", outcomes.GroupBy(o => o.Code ?? ((int)o.Status).ToString()).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}")));
        output.WriteLine($"replays={outcomes.Count(o => o.Replayed)}");

        Assert.Equal(Requests, outcomes.Count);
        Assert.DoesNotContain(outcomes, o => (int)o.Status >= 500);
        Assert.Contains(outcomes, o => o.Code == "INSUFFICIENT_FUNDS");
        Assert.Contains(outcomes, o => o.Code == "DAILY_LIMIT_EXCEEDED");
        Assert.Contains(outcomes, o => o.Replayed);

        await factory.AssertHoldAsync();
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        var succeededKeys = outcomes.Where(o => o.Status == HttpStatusCode.Created).Select(o => o.Key).Distinct().Count();

        // Scoped to this run's wallets: the class database is shared with the other test here.
        var wallets = string.Join(",", customers.Select(c => $"'{c.Wallet}'"));
        Assert.Equal((long)succeededKeys, await ScalarAsync(connection, $"SELECT count(*) FROM ledger_transactions WHERE type = 'TRANSFER' AND source_wallet_id IN ({wallets})"));
        Assert.Equal(0L, await ScalarAsync(connection, $"""
            SELECT count(*) FROM (
              SELECT t.id FROM ledger_transactions t LEFT JOIN audit_log a ON a.transaction_id = t.id
              WHERE t.type = 'TRANSFER' AND t.source_wallet_id IN ({wallets}) GROUP BY t.id HAVING count(a.id) <> 2) bad
            """));
        Assert.Equal(0L, await ScalarAsync(connection, $"""
            SELECT count(*) FROM (
              SELECT wallet_id FROM ledger_entries WHERE entry_type = 'TRANSFER_OUT'
              GROUP BY wallet_id HAVING -sum(amount_kobo) > {dailyLimitKobo}) over_limit
            """));
    }

    [Fact]
    public async Task Twenty_five_identical_requests_with_one_key_post_one_transfer()
    {
        var payer = $"load-dup-{Guid.NewGuid():N}"[..24];
        var source = await factory.FundedWalletAsync(payer, 10_000);
        var destination = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        var key = Guid.NewGuid().ToString();
        var client = factory.CustomerClient(payer);

        // Every request waits on one gate, so they are released together.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = Enumerable.Range(0, 25).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return await client.TransferAsync(source, destination, 700, key);
        })).ToList();
        gate.SetResult();
        var responses = await Task.WhenAll(calls);

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict, r.StatusCode.ToString()));
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        Assert.Equal(1L, await ScalarAsync(connection, $"SELECT count(*) FROM ledger_transactions WHERE source_wallet_id = '{source}'"));
        Assert.Equal(700L, await ScalarAsync(connection, $"SELECT balance_kobo FROM wallets WHERE id = '{destination}'"));
        await factory.AssertHoldAsync();
    }

    // Deterministic request list: one in ten repeats an earlier request's key and body exactly.
    private static List<PlannedTransfer> Plan(Random random, List<(string Customer, Guid Wallet)> customers, long dailyLimitKobo)
    {
        var plan = new List<PlannedTransfer>(Requests);
        for (var i = 0; i < Requests; i++)
        {
            if (i > 20 && random.Next(10) == 0)
            {
                plan.Add(plan[random.Next(i - 20)]);
                continue;
            }

            var rich = random.Next(Wallets) < RichWallets;
            var (low, high) = rich ? (0, RichWallets) : (RichWallets, Wallets);
            var source = random.Next(low, high);
            var destination = random.Next(low, high - 1);
            destination = destination >= source ? destination + 1 : destination;
            // Rich wallets reach the daily limit after a handful of sends; poor ones run dry first.
            var amount = rich
                ? random.NextInt64(dailyLimitKobo * 8 / 100, dailyLimitKobo * 24 / 100)
                : random.NextInt64(dailyLimitKobo * 4 / 1000, dailyLimitKobo * 12 / 1000);
            plan.Add(new PlannedTransfer(customers[source].Customer, customers[source].Wallet, customers[destination].Wallet, amount, Guid.NewGuid().ToString()));
        }

        return plan;
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private sealed record PlannedTransfer(string Customer, Guid Source, Guid Destination, long AmountKobo, string Key);

    private sealed record Outcome(string Key, HttpStatusCode Status, string? Code, bool Replayed);
}
