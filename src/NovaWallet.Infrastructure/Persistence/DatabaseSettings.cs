using Microsoft.Extensions.Options;

namespace NovaWallet.Infrastructure.Persistence;

public sealed class DatabaseSettings
{
    public const string SectionName = "Database";

    /// <summary>Longest a use case waits for a row lock before failing with 503.</summary>
    public int LockTimeoutMilliseconds { get; init; } = 5000;
}

/// <summary>0 would mean "wait forever" to PostgreSQL, silently removing the 503 bound.</summary>
public sealed class DatabaseSettingsValidator : IValidateOptions<DatabaseSettings>
{
    public ValidateOptionsResult Validate(string? name, DatabaseSettings options) =>
        options.LockTimeoutMilliseconds > 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Database:LockTimeoutMilliseconds must be greater than zero.");
}
