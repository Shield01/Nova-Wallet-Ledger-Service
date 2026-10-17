using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NovaWallet.Infrastructure.Persistence;

// Used only by the `dotnet ef` design-time tooling to generate migrations.
public sealed class LedgerDbContextFactory : IDesignTimeDbContextFactory<LedgerDbContext>
{
    private const string DesignTimeConnection = "Host=localhost;Database=novawallet_design";

    public LedgerDbContext CreateDbContext(string[] args) =>
        new(DatabaseSetup.BuildOptions(DesignTimeConnection));
}
