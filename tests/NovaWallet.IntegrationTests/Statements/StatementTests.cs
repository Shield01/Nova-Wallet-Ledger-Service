using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NovaWallet.IntegrationTests.Infrastructure;

namespace NovaWallet.IntegrationTests.Statements;

public class StatementTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => factory.AssertHoldAsync();

    [Fact]
    public async Task Credit_and_transfer_out_appear_newest_first_with_direction_and_counterparty()
    {
        var alice = LedgerApi.NewCustomerId();
        var a = await factory.FundedWalletAsync(alice, 500_000);
        var b = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        var transfer = await factory.CustomerClient(alice).TransferAsync(a, b, 200_000);
        var transferId = (await transfer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transferId").GetGuid();

        var page = await Statement(alice, a);

        Assert.Equal(a, page.GetProperty("walletId").GetGuid());
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        AssertEntry(items[0], "TRANSFER_OUT", "DEBIT", 200_000, 300_000, b);
        Assert.Equal(transferId, items[0].GetProperty("transactionId").GetGuid());
        AssertEntry(items[1], "CREDIT", "CREDIT", 500_000, 500_000, null);
        Assert.True(items[0].GetProperty("createdAt").GetDateTimeOffset() > items[1].GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Transfer_in_shows_the_source_as_counterparty()
    {
        var alice = LedgerApi.NewCustomerId();
        var bob = LedgerApi.NewCustomerId();
        var a = await factory.FundedWalletAsync(alice, 1_000);
        var b = await factory.CreateWalletAsync(bob);
        (await factory.CustomerClient(alice).TransferAsync(a, b, 400)).EnsureSuccessStatusCode();

        var item = Assert.Single((await Statement(bob, b)).GetProperty("items").EnumerateArray());

        AssertEntry(item, "TRANSFER_IN", "CREDIT", 400, 400, a);
    }

    [Fact]
    public async Task Pages_of_the_default_size_cover_every_entry_once()
    {
        var customer = LedgerApi.NewCustomerId();
        var wallet = await SeedCreditsAsync(customer, 45);

        var pages = await WalkAsync(customer, wallet, query: "");

        Assert.Equal([20, 20, 5], pages.Select(p => p.Count).ToArray());
        var ids = pages.SelectMany(p => p).ToList();
        Assert.Equal(45, ids.Distinct().Count());
        Assert.Equal(ids.OrderByDescending(i => i), ids);
    }

    [Fact]
    public async Task Entries_posted_while_paging_cause_no_repeat_or_skip()
    {
        var customer = LedgerApi.NewCustomerId();
        var wallet = await SeedCreditsAsync(customer, 10);
        var existing = (await Statement(customer, wallet, "limit=100")).GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("entryId").GetInt64()).ToList();

        var first = await Statement(customer, wallet, "limit=4");
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        for (var i = 0; i < 3; i++)
        {
            (await factory.InboundSystemClient().CreditAsync(wallet, 1)).EnsureSuccessStatusCode();
        }

        var rest = await WalkAsync(customer, wallet, "limit=4", first.GetProperty("nextCursor").GetString());
        var seen = Ids(first).Concat(rest.SelectMany(p => p)).ToList();

        Assert.Equal(existing, seen);
    }

    [Fact]
    public async Task Equal_timestamps_are_ordered_by_entry_id_descending()
    {
        var customer = LedgerApi.NewCustomerId();
        var wallet = await factory.CreateWalletAsync(customer);
        for (var i = 0; i < 7; i++)
        {
            (await factory.InboundSystemClient().CreditAsync(wallet, 10)).EnsureSuccessStatusCode();
        }

        var pages = await WalkAsync(customer, wallet, "limit=3");
        var all = (await Statement(customer, wallet)).GetProperty("items").EnumerateArray().ToList();

        Assert.Single(all.Select(e => e.GetProperty("createdAt").GetDateTimeOffset()).Distinct());
        var ids = pages.SelectMany(p => p).ToList();
        Assert.Equal(7, ids.Distinct().Count());
        Assert.Equal(ids.OrderByDescending(i => i), ids);
    }

    [Fact]
    public async Task Balance_after_matches_the_running_balance()
    {
        var customer = LedgerApi.NewCustomerId();
        var wallet = await factory.FundedWalletAsync(customer, 300);
        var other = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        (await factory.CustomerClient(customer).TransferAsync(wallet, other, 120)).EnsureSuccessStatusCode();
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        (await factory.InboundSystemClient().CreditAsync(wallet, 50)).EnsureSuccessStatusCode();

        var items = (await Statement(customer, wallet)).GetProperty("items").EnumerateArray().Reverse().ToList();

        var running = 0L;
        foreach (var item in items)
        {
            var amount = item.GetProperty("amountKobo").GetInt64();
            Assert.True(amount > 0);
            running += item.GetProperty("direction").GetString() == "CREDIT" ? amount : -amount;
            Assert.Equal(running, item.GetProperty("balanceAfterKobo").GetInt64());
        }

        Assert.Equal(230, running);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Limit_boundaries_are_accepted(int limit)
    {
        var customer = LedgerApi.NewCustomerId();
        var wallet = await SeedCreditsAsync(customer, 3);

        var page = await Statement(customer, wallet, $"limit={limit}");

        Assert.Equal(Math.Min(limit, 3), page.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=ten")]
    [InlineData("cursor=garbage!!")]
    [InlineData("cursor=MTIzOmFiYw")]
    public async Task Invalid_query_is_rejected(string query)
    {
        var customer = LedgerApi.NewCustomerId();
        var wallet = await factory.CreateWalletAsync(customer);

        var response = await factory.CustomerClient(customer).GetAsync($"/api/v1/wallets/{wallet}/statement?{query}");

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Other_customers_unknown_wallets_and_the_system_account_are_not_found()
    {
        var owner = LedgerApi.NewCustomerId();
        var wallet = await factory.CreateWalletAsync(owner);
        var stranger = factory.CustomerClient(LedgerApi.NewCustomerId());

        foreach (var id in new[] { wallet, Guid.NewGuid(), Guid.Parse(LedgerApiMoney.SystemAccountId) })
        {
            var response = await stranger.GetAsync($"/api/v1/wallets/{id}/statement");
            await response.AssertProblemAsync(HttpStatusCode.NotFound, "WALLET_NOT_FOUND");
        }
    }

    [Fact]
    public async Task Anonymous_and_wrong_scope_callers_are_refused()
    {
        var owner = LedgerApi.NewCustomerId();
        var wallet = await factory.CreateWalletAsync(owner);
        var wrongScope = factory.CreateClient().WithBearer(factory.Tokens.For(owner, TestTokens.CreditScope));

        await (await factory.CreateClient().GetAsync($"/api/v1/wallets/{wallet}/statement"))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await (await wrongScope.GetAsync($"/api/v1/wallets/{wallet}/statement"))
            .AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    private static void AssertEntry(JsonElement item, string type, string direction, long amount, long balanceAfter, Guid? counterparty)
    {
        Assert.Equal(type, item.GetProperty("type").GetString());
        Assert.Equal(direction, item.GetProperty("direction").GetString());
        Assert.Equal(amount, item.GetProperty("amountKobo").GetInt64());
        Assert.Equal(balanceAfter, item.GetProperty("balanceAfterKobo").GetInt64());
        var actual = item.GetProperty("counterpartyWalletId");
        if (counterparty is null)
        {
            Assert.Equal(JsonValueKind.Null, actual.ValueKind);
        }
        else
        {
            Assert.Equal(counterparty, actual.GetGuid());
        }
    }

    private static IEnumerable<long> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("entryId").GetInt64());

    private async Task<Guid> SeedCreditsAsync(string customer, int count)
    {
        var wallet = await factory.CreateWalletAsync(customer);
        for (var i = 0; i < count; i++)
        {
            // Advance on some credits only, so pages contain both distinct and equal timestamps.
            if (i % 3 == 0)
            {
                factory.Clock.Advance(TimeSpan.FromMilliseconds(10));
            }

            (await factory.InboundSystemClient().CreditAsync(wallet, 100 + i)).EnsureSuccessStatusCode();
        }

        return wallet;
    }

    private async Task<JsonElement> Statement(string customer, Guid wallet, string query = "")
    {
        var response = await factory.CustomerClient(customer).GetAsync($"/api/v1/wallets/{wallet}/statement?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<List<List<long>>> WalkAsync(string customer, Guid wallet, string query, string? cursor = null)
    {
        var pages = new List<List<long>>();
        do
        {
            var suffix = cursor is null ? query : $"{query}&cursor={cursor}";
            var page = await Statement(customer, wallet, suffix);
            pages.Add(Ids(page).ToList());
            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null && pages.Count < 50);

        return pages;
    }
}
