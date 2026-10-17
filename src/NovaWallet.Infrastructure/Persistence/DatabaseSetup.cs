using Microsoft.EntityFrameworkCore;

namespace NovaWallet.Infrastructure.Persistence;

public static class DatabaseSetup
{
    public const string RuntimeConnectionName = "Ledger";
    public const string MigrationsConnectionName = "LedgerMigrations";

    public static DbContextOptions<LedgerDbContext> BuildOptions(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<LedgerDbContext>();
        Configure(builder, connectionString);
        return builder.Options;
    }

    public static void Configure(DbContextOptionsBuilder builder, string connectionString) =>
        builder
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
