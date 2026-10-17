using System.Net.Http.Json;
using System.Text.Json;

namespace NovaWallet.IntegrationTests.Infrastructure;

/// <summary>Small typed helpers over the HTTP API, shared by feature tests.</summary>
public static class LedgerApi
{
    public static HttpClient CustomerClient(this NovaWalletApiFactory factory, string customerId) =>
        factory.CreateClient().WithBearer(factory.Tokens.For(customerId, TestTokens.WalletScope));

    public static async Task<Guid> CreateWalletAsync(this NovaWalletApiFactory factory, string customerId)
    {
        var response = await factory.CustomerClient(customerId).PostAsJsonAsync("/api/v1/wallets", new { customerId });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("walletId").GetGuid();
    }

    public static string NewCustomerId() => $"cust-{Guid.NewGuid():N}"[..32];
}
