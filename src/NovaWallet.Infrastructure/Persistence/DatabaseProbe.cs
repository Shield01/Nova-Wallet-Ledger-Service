using Microsoft.EntityFrameworkCore;

namespace NovaWallet.Infrastructure.Persistence;

/// <summary>Runs one trivial query, for the readiness check. Failures surface as exceptions.</summary>
public sealed class DatabaseProbe(LedgerDbContext db)
{
    public Task QueryAsync(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
}
