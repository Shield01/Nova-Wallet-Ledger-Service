using System.Net;
using NovaWallet.IntegrationTests.Infrastructure;

namespace NovaWallet.IntegrationTests.Health;

public class HealthTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    private static readonly string UnreachablePassword = $"pw-{Guid.NewGuid():N}";
    private static readonly string Unreachable = $"Host=127.0.0.1;Port=1;Database=nowhere_db;Username=nobody;Password={UnreachablePassword};Timeout=2";

    [Fact]
    public async Task Ready_and_live_answer_200_anonymously_when_the_database_is_up()
    {
        var ready = await factory.CreateClient().GetAsync("/health/ready");
        var live = await factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Contains("Healthy", await ready.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_is_503_problem_details_without_internals_when_the_database_is_unreachable()
    {
        using var down = factory.WithSettings(new Dictionary<string, string?> { ["ConnectionStrings:Ledger"] = Unreachable });

        var ready = await down.CreateClient().GetAsync("/health/ready");

        var problem = await ready.AssertProblemAsync(HttpStatusCode.ServiceUnavailable, "SERVICE_UNAVAILABLE");
        var body = problem.GetRawText();
        foreach (var secret in new[] { "127.0.0.1", "Port=1", "nowhere_db", "nobody", UnreachablePassword, "Exception", "refused" })
        {
            Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Live_stays_200_when_the_database_is_unreachable()
    {
        using var down = factory.WithSettings(new Dictionary<string, string?> { ["ConnectionStrings:Ledger"] = Unreachable });

        var live = await down.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }
}
