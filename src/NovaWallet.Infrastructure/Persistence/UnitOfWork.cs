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
        try
        {
            // Bounded lock waits: a stuck transaction turns into a fast 503 instead of a pile-up.
            var lockTimeout = $"{settings.Value.LockTimeoutMilliseconds}ms";
            await db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {lockTimeout}, true)", cancellationToken);

            var result = await work(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            // Once the writes are flushed, a client disconnect must not abort the commit and turn
            // a completed money movement into a confusing 500.
            await transaction.CommitAsync(CancellationToken.None);
            return result;
        }
        catch
        {
            // Entities tracked during the failed work must not leak into anything else in this scope.
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<ISavepoint> SavepointAsync(CancellationToken cancellationToken)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Savepoints only exist inside a unit of work.");
        var name = $"sp_{Guid.NewGuid():N}";
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CreateSavepointAsync(name, cancellationToken);
        return new Savepoint(db, transaction, name);
    }

    private sealed class Savepoint(LedgerDbContext db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, string name) : ISavepoint
    {
        public async Task RollbackAsync(CancellationToken cancellationToken)
        {
            db.ChangeTracker.Clear();
            await transaction.RollbackToSavepointAsync(name, cancellationToken);
        }
    }
}
