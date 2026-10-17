using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NovaWallet.IntegrationTests.Infrastructure;

namespace NovaWallet.IntegrationTests.Transfers;

public class DailyLimitTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>, IAsyncLifetime
{
    private const long Limit = 50_000_000;
    private static readonly TimeSpan Wat = TimeSpan.FromHours(1);

    // The fake clock is shared by the class and can only move forward, so every test starts on a
    // fresh WAT day at 10:00 and works relative to it.
    private DateTimeOffset _today;

    public Task InitializeAsync()
    {
        var watNow = factory.Clock.GetUtcNow().ToOffset(Wat);
        _today = new DateTimeOffset(watNow.Year, watNow.Month, watNow.Day, 0, 0, 0, Wat).AddDays(1);
        factory.Clock.SetUtcNow(_today.AddHours(10).ToUniversalTime());
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => factory.AssertHoldAsync();

    [Fact]
    public async Task Reaching_the_limit_exactly_is_allowed_and_one_kobo_more_is_refused()
    {
        var (a, aId, bId) = await PairAsync(100_000_000);
        Assert.Equal(HttpStatusCode.Created, (await a.TransferAsync(aId, bId, 49_000_000)).StatusCode);

        var exact = await a.TransferAsync(aId, bId, 1_000_000);
        var over = await a.TransferAsync(aId, bId, 1);

        Assert.Equal(HttpStatusCode.Created, exact.StatusCode);
        var problem = await over.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "DAILY_LIMIT_EXCEEDED");
        Assert.Equal(Limit, problem.GetProperty("dailyLimitKobo").GetInt64());
        Assert.Equal(0, problem.GetProperty("remainingKobo").GetInt64());
        Assert.Equal(50_000_000, await SentAsync(bId));
    }

    [Fact]
    public async Task A_single_transfer_above_the_limit_is_refused_in_full()
    {
        var (a, aId, bId) = await PairAsync(100_000_000);

        var response = await a.TransferAsync(aId, bId, Limit + 1);

        var problem = await response.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "DAILY_LIMIT_EXCEEDED");
        Assert.Equal(Limit, problem.GetProperty("remainingKobo").GetInt64());
        Assert.Equal(0, await SentAsync(bId));
    }

    [Fact]
    public async Task The_day_is_the_WAT_calendar_day_and_resets_at_midnight()
    {
        var (a, aId, bId) = await PairAsync(100_000_000);
        factory.Clock.SetUtcNow(_today.AddDays(1).AddMilliseconds(-1).ToUniversalTime());
        Assert.Equal(HttpStatusCode.Created, (await a.TransferAsync(aId, bId, Limit)).StatusCode);
        var stillToday = await a.TransferAsync(aId, bId, 1);

        factory.Clock.SetUtcNow(_today.AddDays(1).ToUniversalTime());
        var nextDay = await a.TransferAsync(aId, bId, 1_000);

        await stillToday.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "DAILY_LIMIT_EXCEEDED");
        Assert.Equal(HttpStatusCode.Created, nextDay.StatusCode);
    }

    [Fact]
    public async Task Midnight_is_WAT_not_UTC()
    {
        var (a, aId, bId) = await PairAsync(100_000_000);
        // 22:30 UTC is 23:30 WAT today; 23:30 UTC is already 00:30 WAT tomorrow.
        var todayUtcDate = _today.ToUniversalTime().AddHours(1);
        factory.Clock.SetUtcNow(todayUtcDate.AddHours(22.5));
        Assert.Equal(HttpStatusCode.Created, (await a.TransferAsync(aId, bId, Limit)).StatusCode);

        factory.Clock.SetUtcNow(todayUtcDate.AddHours(23.5));
        var afterWatMidnight = await a.TransferAsync(aId, bId, 1_000);

        Assert.Equal(HttpStatusCode.Created, afterWatMidnight.StatusCode);
    }

    [Fact]
    public async Task Credits_incoming_transfers_and_refusals_do_not_count()
    {
        var (a, aId, bId) = await PairAsync(60_000_000);
        var (b, _, _) = (factory.CustomerClient(await OwnerOfAsync(bId)), bId, aId);
        await factory.InboundSystemClient().CreditAsync(bId, 10_000_000);
        await b.TransferAsync(bId, aId, 10_000_000);
        await a.TransferAsync(aId, bId, 999_000_000);

        var full = await a.TransferAsync(aId, bId, Limit);

        Assert.Equal(HttpStatusCode.Created, full.StatusCode);
    }

    [Fact]
    public async Task Replayed_success_is_not_counted_twice()
    {
        var (a, aId, bId) = await PairAsync(100_000_000);
        var key = Guid.NewGuid().ToString();
        await a.TransferAsync(aId, bId, 30_000_000, key);
        await a.TransferAsync(aId, bId, 30_000_000, key);

        var rest = await a.TransferAsync(aId, bId, 20_000_000);

        Assert.Equal(HttpStatusCode.Created, rest.StatusCode);
    }

    [Fact]
    public async Task Insufficient_funds_wins_over_the_limit()
    {
        var (a, aId, bId) = await PairAsync(1_000);

        var response = await a.TransferAsync(aId, bId, Limit + 1);

        await response.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "INSUFFICIENT_FUNDS");
    }

    [Fact]
    public async Task Limit_refusal_is_stored_and_replayed()
    {
        var (a, aId, bId) = await PairAsync(100_000_000);
        var key = Guid.NewGuid().ToString();

        var first = await a.TransferAsync(aId, bId, Limit + 1, key);
        var replay = await a.TransferAsync(aId, bId, Limit + 1, key);

        await first.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "DAILY_LIMIT_EXCEEDED");
        await replay.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "DAILY_LIMIT_EXCEEDED");
        Assert.True(replay.Headers.Contains("Idempotent-Replayed"));
    }

    [Fact]
    public async Task Concurrent_transfers_never_jointly_exceed_the_limit()
    {
        var (a, aId, bId) = await PairAsync(100_000_000);

        var responses = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => a.TransferAsync(aId, bId, 2_000_000)));

        Assert.Equal(25, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        Assert.Equal(Limit, await SentAsync(bId));
    }

    [Fact]
    public async Task Configured_limit_applies_without_code_changes()
    {
        using var strict = factory.WithSettings(new Dictionary<string, string?> { ["Limits:DailyOutboundKobo"] = "1000" });
        var customerId = LedgerApi.NewCustomerId();
        var aId = await factory.FundedWalletAsync(customerId, 5_000);
        var bId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        var a = strict.CreateClient().WithBearer(factory.Tokens.For(customerId, TestTokens.WalletScope));

        var ok = await a.TransferAsync(aId, bId, 1_000);
        var over = await a.TransferAsync(aId, bId, 1);

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var problem = await over.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "DAILY_LIMIT_EXCEEDED");
        Assert.Equal(1_000, problem.GetProperty("dailyLimitKobo").GetInt64());
    }

    private readonly Dictionary<Guid, string> _owners = [];

    private async Task<(HttpClient Client, Guid Source, Guid Destination)> PairAsync(long fundsKobo)
    {
        var customerId = LedgerApi.NewCustomerId();
        var aId = await factory.FundedWalletAsync(customerId, fundsKobo);
        var bCustomer = LedgerApi.NewCustomerId();
        var bId = await factory.CreateWalletAsync(bCustomer);
        _owners[bId] = bCustomer;
        return (factory.CustomerClient(customerId), aId, bId);
    }

    private Task<string> OwnerOfAsync(Guid walletId) => Task.FromResult(_owners[walletId]);

    private async Task<long> SentAsync(Guid destination)
    {
        var owner = _owners[destination];
        var balance = await factory.CustomerClient(owner).GetFromJsonAsync<JsonElement>($"/api/v1/wallets/{destination}/balance");
        return balance.GetProperty("balanceKobo").GetInt64();
    }
}
