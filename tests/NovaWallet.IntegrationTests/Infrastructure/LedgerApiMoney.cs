using System.Net.Http.Json;

namespace NovaWallet.IntegrationTests.Infrastructure;

public static class LedgerApiMoney
{
    public const string SystemAccountId = "00000000-0000-0000-0000-000000000001";

    public static HttpClient InboundSystemClient(this NovaWalletApiFactory factory, string subject = "nip-gateway") =>
        factory.CreateClient().WithBearer(factory.Tokens.For(subject, TestTokens.CreditScope));

    public static Task<HttpResponseMessage> CreditAsync(this HttpClient client, Guid walletId, long amountKobo, string? key = null, string? correlationId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/wallets/{walletId}/credits")
        {
            Content = JsonContent.Create(new { amountKobo }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        return client.SendAsync(request);
    }

    public static async Task<Guid> FundedWalletAsync(this NovaWalletApiFactory factory, string customerId, long amountKobo)
    {
        var walletId = await factory.CreateWalletAsync(customerId);
        if (amountKobo > 0)
        {
            var response = await factory.InboundSystemClient().CreditAsync(walletId, amountKobo);
            response.EnsureSuccessStatusCode();
        }

        return walletId;
    }
}
