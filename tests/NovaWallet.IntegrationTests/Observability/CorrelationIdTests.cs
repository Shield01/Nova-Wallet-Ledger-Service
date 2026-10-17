using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NovaWallet.IntegrationTests.Infrastructure;

namespace NovaWallet.IntegrationTests.Observability;

public class CorrelationIdTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    private const string Header = "X-Correlation-ID";

    [Fact]
    public async Task Valid_inbound_id_is_echoed_and_scopes_every_log_line_of_the_request()
    {
        var (client, logs) = Capturing();
        client.DefaultRequestHeaders.Add(Header, "abc-123");

        var response = await client.GetAsync("/health/live");

        Assert.Equal("abc-123", Single(response, Header));
        var requestLines = logs.Entries.Where(e => Equals(e.Scope.GetValueOrDefault("CorrelationId"), "abc-123")).ToList();
        Assert.NotEmpty(requestLines);
        Assert.All(requestLines, e => Assert.True(e.Scope.ContainsKey("TraceId")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has spaces")]
    [InlineData("semi;colon")]
    public async Task Invalid_inbound_id_is_replaced_by_a_generated_one(string inbound)
    {
        var client = factory.CreateClient();
        if (inbound.Length > 0)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(Header, inbound);
        }

        var response = await client.GetAsync("/health/live");

        var echoed = Single(response, Header);
        Assert.NotEqual(inbound, echoed);
        Assert.Matches("^[A-Za-z0-9-]{1,64}$", echoed);
    }

    [Fact]
    public async Task Sixty_four_character_inbound_id_is_kept()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(Header, new string('a', 64));

        var response = await client.GetAsync("/health/live");

        Assert.Equal(new string('a', 64), Single(response, Header));
    }

    [Fact]
    public async Task Over_long_inbound_id_is_replaced()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(Header, new string('a', 65));

        var response = await client.GetAsync("/health/live");

        Assert.NotEqual(new string('a', 65), Single(response, Header));
    }

    [Theory]
    [InlineData("/test/auth/wallet", null, HttpStatusCode.Unauthorized)]
    [InlineData("/test/auth/audit", "wallet", HttpStatusCode.Forbidden)]
    [InlineData("/test/throw", null, HttpStatusCode.InternalServerError)]
    public async Task Problem_details_carry_the_response_correlation_id(string path, string? scope, HttpStatusCode status)
    {
        var client = factory.CreateClient();
        if (scope is not null)
        {
            client.WithBearer(factory.Tokens.For("cust-001", scope));
        }

        var response = await client.GetAsync(path);

        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(Single(response, Header), problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Each_request_writes_one_completion_line_with_route_and_status()
    {
        var (client, logs) = Capturing();
        client.DefaultRequestHeaders.Add(Header, "completion-1");

        await client.GetAsync("/health/live");

        var completion = Assert.Single(logs.Entries, e =>
            e.Category.EndsWith("CorrelationIdMiddleware", StringComparison.Ordinal)
            && Equals(e.Scope.GetValueOrDefault("CorrelationId"), "completion-1"));
        Assert.Contains("GET /health/live responded 200", completion.Message);
    }

    [Fact]
    public async Task Tokens_and_request_bodies_never_reach_the_logs()
    {
        var (client, logs) = Capturing();
        var token = factory.Tokens.For("cust-001", "wallet");
        client.WithBearer(token);
        const string bodyMarker = "body-marker-91f2";

        await client.PostAsJsonAsync("/test/validate", new { customerName = bodyMarker, amountKobo = 0 });
        await client.PostAsJsonAsync("/dev/token", new { subject = bodyMarker, scopes = new[] { "nope" } });

        Assert.NotEmpty(logs.Entries);
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains(token, StringComparison.Ordinal) || (e.Exception?.Contains(token, StringComparison.Ordinal) ?? false));
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains(bodyMarker, StringComparison.Ordinal));
    }

    private (HttpClient Client, CapturingLoggerProvider Logs) Capturing()
    {
        var logs = new CapturingLoggerProvider();
        var host = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(logs);
            logging.SetMinimumLevel(LogLevel.Information);
        }));
        return (host.CreateClient(), logs);
    }

    private static string Single(HttpResponseMessage response, string header) =>
        Assert.Single(response.Headers.GetValues(header));
}
