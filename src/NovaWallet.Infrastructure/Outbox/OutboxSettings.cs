namespace NovaWallet.Infrastructure.Outbox;

public sealed class OutboxSettings
{
    public const string SectionName = "Outbox";

    /// <summary>Below this the poller would spin on an idle database for no benefit.</summary>
    public const int MinPollIntervalMilliseconds = 10;

    public int PollIntervalMilliseconds { get; init; } = 1000;

    /// <summary>Failed publishes after which a message is left unpublished and an error is logged.</summary>
    public int MaxAttempts { get; init; } = 10;

    /// <summary>Messages claimed per poll. A full batch is followed straight away by another poll.</summary>
    public int BatchSize { get; init; } = 100;
}
