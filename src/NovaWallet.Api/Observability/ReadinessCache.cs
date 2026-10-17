using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NovaWallet.Api.Observability;

/// <summary>
/// Reuses the last readiness answer for a short time and lets only one probe run at once, so
/// anonymous callers can't turn /health/ready into a load on the database connection pool.
/// </summary>
public sealed class ReadinessCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Entry? _entry;

    public async Task<HealthCheckResult> GetAsync(Func<CancellationToken, Task<HealthCheckResult>> probe, TimeSpan reuseFor, CancellationToken cancellationToken)
    {
        if (Fresh() is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Fresh() is { } again)
            {
                return again;
            }

            var result = await probe(cancellationToken);
            _entry = new Entry(result, Stopwatch.GetTimestamp() + (long)(reuseFor.TotalSeconds * Stopwatch.Frequency));
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private HealthCheckResult? Fresh() =>
        _entry is { } entry && Stopwatch.GetTimestamp() < entry.ExpiresAt ? entry.Result : null;

    private sealed record Entry(HealthCheckResult Result, long ExpiresAt);
}
