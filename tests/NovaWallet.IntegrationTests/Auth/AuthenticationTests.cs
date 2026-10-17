using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NovaWallet.IntegrationTests.Infrastructure;

namespace NovaWallet.IntegrationTests.Auth;

public class AuthenticationTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    private const string Subject = "cust-001";


    [Fact]
    public async Task Missing_token_is_unauthenticated_with_bearer_challenge()
    {
        var response = await factory.CreateClient().GetAsync("/test/auth/wallet");

        await response.AssertProblemAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }

    [Fact]
    public async Task Malformed_token_is_unauthenticated()
    {
        await AssertUnauthenticated("not-a-jwt");
    }

    [Fact]
    public async Task Expired_token_is_unauthenticated()
    {
        var past = DateTime.UtcNow.AddHours(-2);
        await AssertUnauthenticated(factory.Tokens.Create(Subject, [TestTokens.WalletScope], notBefore: past, expires: past.AddMinutes(15)));
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_unauthenticated()
    {
        await AssertUnauthenticated(factory.Tokens.Create(Subject, [TestTokens.WalletScope], overrideSigningKey: new string('k', 64)));
    }

    [Fact]
    public async Task Token_from_another_issuer_is_unauthenticated()
    {
        await AssertUnauthenticated(factory.Tokens.Create(Subject, [TestTokens.WalletScope], overrideIssuer: "someone-else"));
    }

    [Fact]
    public async Task Token_for_another_audience_is_unauthenticated()
    {
        await AssertUnauthenticated(factory.Tokens.Create(Subject, [TestTokens.WalletScope], overrideAudience: "another-api"));
    }

    [Fact]
    public async Task Token_signed_with_another_algorithm_is_unauthenticated()
    {
        await AssertUnauthenticated(factory.Tokens.Create(Subject, [TestTokens.WalletScope], algorithm: SecurityAlgorithms.HmacSha384));
    }

    [Fact]
    public async Task Valid_token_without_the_scope_is_forbidden()
    {
        var response = await Client(factory.Tokens.For(Subject, TestTokens.WalletScope)).GetAsync("/test/auth/audit");

        await response.AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Space_delimited_scopes_satisfy_every_listed_policy()
    {
        var client = Client(factory.Tokens.For(Subject, TestTokens.WalletScope, TestTokens.AuditScope));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test/auth/wallet")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test/auth/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test/auth/credit")).StatusCode);
    }

    [Fact]
    public async Task Endpoint_without_a_policy_still_requires_a_token()
    {
        var anonymous = await factory.CreateClient().GetAsync("/test/auth/any");
        var authenticated = await Client(factory.Tokens.For(Subject, TestTokens.AuditScope)).GetAsync("/test/auth/any");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
    }

    [Fact]
    public async Task Application_code_sees_the_token_subject()
    {
        var body = await Client(factory.Tokens.For(Subject, TestTokens.WalletScope)).GetFromJsonAsync<JsonElement>("/test/auth/wallet");

        Assert.Equal(Subject, body.GetProperty("subject").GetString());
    }

    private async Task AssertUnauthenticated(string token)
    {
        var response = await Client(token).GetAsync("/test/auth/wallet");
        await response.AssertProblemAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    private HttpClient Client(string token) => factory.CreateClient().WithBearer(token);
}
