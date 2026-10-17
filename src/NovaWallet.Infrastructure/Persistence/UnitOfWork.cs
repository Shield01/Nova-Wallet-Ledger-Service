using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NovaWallet.Application.Abstractions;

namespace NovaWallet.Infrastructure.Persistence;

public sealed class UnitOfWork(LedgerDbContext db, IOptions<DatabaseSettings> settings) : IUnitOfWork
{
    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await work(cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        // Bounded lock waits: a stuck transaction turns into a fast 503 instead of a pile-up.
        var lockTimeout = $"{settings.Value.LockTimeoutMilliseconds}ms";
        await db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {lockTimeout}, true)", cancellationToken);

        var result = await work(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
