using Microsoft.Extensions.Diagnostics.HealthChecks;
using NovaWallet.Api.Observability;

namespace NovaWallet.IntegrationTests.Health;

public class ReadinessCacheTests
{
    [Fact]
    public async Task A_flood_of_probes_runs_the_database_check_once_per_interval()
    {
        var cache = new ReadinessCache();
        var calls = 0;
        Task<HealthCheckResult> Probe(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(HealthCheckResult.Healthy());
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() => cache.GetAsync(Probe, TimeSpan.FromMinutes(1), CancellationToken.None))));

        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.Equal(HealthStatus.Healthy, r.Status));
    }

    [Fact]
    public async Task An_expired_answer_is_checked_again()
    {
        var cache = new ReadinessCache();
        var calls = 0;
        Task<HealthCheckResult> Probe(CancellationToken _) => Task.FromResult(Interlocked.Increment(ref calls) == 1 ? HealthCheckResult.Unhealthy() : HealthCheckResult.Healthy());

        var first = await cache.GetAsync(Probe, TimeSpan.Zero, CancellationToken.None);
        var second = await cache.GetAsync(Probe, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, first.Status);
        Assert.Equal(HealthStatus.Healthy, second.Status);
        Assert.Equal(2, calls);
    }
}
