namespace NovaWallet.Application.Abstractions;

public static class ClockExtensions
{
    private const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    /// <summary>
    /// "Now" at the precision PostgreSQL stores (microseconds), so a timestamp returned to a client
    /// is exactly the one written to the ledger and the audit trail.
    /// </summary>
    public static DateTimeOffset GetStoredUtcNow(this TimeProvider clock)
    {
        var now = clock.GetUtcNow().ToUniversalTime();
        return new DateTimeOffset(now.Ticks - (now.Ticks % TicksPerMicrosecond), TimeSpan.Zero);
    }
}
