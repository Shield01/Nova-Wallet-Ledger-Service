using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NovaWallet.IntegrationTests.Infrastructure;

namespace NovaWallet.IntegrationTests.Wallets;

public class GetBalanceTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    private const string SystemAccountId = "00000000-0000-0000-0000-000000000001";

    [Fact]
    public async Task Owner_reads_balance_in_kobo_with_currency_and_as_of_time()
    {
        var customerId = LedgerApi.NewCustomerId();
        var walletId = await factory.CreateWalletAsync(customerId);

        var response = await factory.CustomerClient(customerId).GetAsync($"/api/v1/wallets/{walletId}/balance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(walletId, body.GetProperty("walletId").GetGuid());
        Assert.Equal(JsonValueKind.Number, body.GetProperty("balanceKobo").ValueKind);
        Assert.Equal(0, body.GetProperty("balanceKobo").GetInt64());
        Assert.Equal("NGN", body.GetProperty("currency").GetString());
        Assert.Equal(factory.Clock.GetUtcNow(), body.GetProperty("asOf").GetDateTimeOffset());
    }

    [Fact]
    public async Task Another_customers_wallet_is_not_found()
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());

        var response = await factory.CustomerClient(LedgerApi.NewCustomerId()).GetAsync($"/api/v1/wallets/{walletId}/balance");

        await response.AssertProblemAsync(HttpStatusCode.NotFound, "WALLET_NOT_FOUND");
    }

    [Theory]
    [InlineData("8f14e45f-ceea-4e67-a8f0-6d2a3c1b9e01")]
    [InlineData(SystemAccountId)]
    public async Task Unknown_wallet_and_system_account_are_not_found(string walletId)
    {
        var response = await factory.CustomerClient("cust-001").GetAsync($"/api/v1/wallets/{walletId}/balance");

        await response.AssertProblemAsync(HttpStatusCode.NotFound, "WALLET_NOT_FOUND");
    }

    [Fact]
    public async Task Malformed_wallet_id_is_an_unknown_route()
    {
        var response = await factory.CustomerClient("cust-001").GetAsync("/api/v1/wallets/not-a-guid/balance");

        await response.AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Missing_token_and_missing_scope_are_refused()
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());

        var anonymous = await factory.CreateClient().GetAsync($"/api/v1/wallets/{walletId}/balance");
        var auditor = await factory.CreateClient().WithBearer(factory.Tokens.For("cust-001", TestTokens.AuditScope))
            .GetAsync($"/api/v1/wallets/{walletId}/balance");

        await anonymous.AssertProblemAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await auditor.AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }
}
