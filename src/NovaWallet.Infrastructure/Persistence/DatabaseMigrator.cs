using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace NovaWallet.Infrastructure.Persistence;

public static class DatabaseMigrator
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private const int MaxAttempts = 15;

    public static async Task MigrateAsync(string ownerConnectionString, ILogger logger, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var context = new LedgerDbContext(DatabaseSetup.BuildOptions(ownerConnectionString));
                await context.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Database migrated after {Attempts} attempt(s)", attempt);
                return;
            }
            catch (NpgsqlException ex) when (ex.IsTransient && attempt < MaxAttempts)
            {
                logger.LogWarning("Database not reachable yet (attempt {Attempt} of {MaxAttempts}); retrying", attempt, MaxAttempts);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }
}
