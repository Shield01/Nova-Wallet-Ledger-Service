namespace NovaWallet.Api.Observability;

public sealed class HealthSettings
{
    public const string SectionName = "Health";

    /// <summary>How long readiness waits for the database before reporting not ready.</summary>
    public int ReadinessTimeoutMilliseconds { get; init; } = 3000;

    /// <summary>How long a readiness answer is reused, so a flood of probes can't load the database.</summary>
    public int ReadinessCacheMilliseconds { get; init; } = 1000;
}
