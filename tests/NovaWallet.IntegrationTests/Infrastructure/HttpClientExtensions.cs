using System.Net.Http.Headers;

namespace NovaWallet.IntegrationTests.Infrastructure;

public static class HttpClientExtensions
{
    public static HttpClient WithBearer(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
