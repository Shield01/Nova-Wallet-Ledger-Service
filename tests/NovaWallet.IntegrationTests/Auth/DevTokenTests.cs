using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using NovaWallet.Api.Auth;
using NovaWallet.IntegrationTests.Infrastructure;

namespace NovaWallet.IntegrationTests.Auth;

public class DevTokenTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    [Fact]
    public async Task Issued_token_is_accepted_and_lives_for_the_configured_lifetime()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/dev/token", new { subject = "cust-001", scopes = new[] { "wallet" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
        var token = body.GetProperty("accessToken").GetString()!;
        var jwt = new JsonWebToken(token);
        Assert.Equal(TimeSpan.FromMinutes(15), jwt.ValidTo - jwt.IssuedAt);
        Assert.Equal("cust-001", jwt.Subject);

        var protectedCall = await factory.CreateClient().WithBearer(token).GetAsync("/test/auth/wallet");
        Assert.Equal(HttpStatusCode.OK, protectedCall.StatusCode);
    }

    [Theory]
    [InlineData("""{"subject":"cust-001","scopes":["admin"]}""", "scopes")]
    [InlineData("""{"subject":"cust-001","scopes":[]}""", "scopes")]
    [InlineData("""{"subject":"has spaces","scopes":["wallet"]}""", "subject")]
    [InlineData("""{"subject":"","scopes":["wallet"]}""", "subject")]
    [InlineData("""{"subject":"cust-001","scopes":["wallet","ledger:credit"]}""", "scopes")]
    public async Task Invalid_requests_get_no_token(string json, string field)
    {
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await factory.CreateClient().PostAsync("/dev/token", content);

        var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), problem.GetRawText());
    }

    [Fact]
    public async Task Disabled_issuer_has_no_endpoint()
    {
        using var disabled = factory.WithSettings(new Dictionary<string, string?> { ["Auth:DevIssuer:Enabled"] = "false" });

        var request = new { subject = "cust-001", scopes = new[] { "wallet" } };
        var anonymous = await disabled.CreateClient().PostAsJsonAsync("/dev/token", request);
        var authenticated = await disabled.CreateClient()
            .WithBearer(factory.Tokens.For("cust-001", TestTokens.WalletScope))
            .PostAsJsonAsync("/dev/token", request);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, authenticated.StatusCode);
    }
}
