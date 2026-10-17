namespace NovaWallet.Infrastructure.Persistence;

public sealed class DatabaseSettings
{
    public const string SectionName = "Database";

    /// <summary>Longest a use case waits for a row lock before failing with 503.</summary>
    public int LockTimeoutMilliseconds { get; init; } = 5000;
}
