using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.Idempotency;
using NovaWallet.Domain.Idempotency;

namespace NovaWallet.Infrastructure.Persistence.Stores;

public sealed class IdempotencyStore(LedgerDbContext db) : IIdempotencyStore
{
    public async Task<bool> TryLockAsync(string callerId, string key, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Idempotency locks are transaction-scoped; call this inside a unit of work.");
        }

        // A 64-bit hash of caller and key; the lock is released when the transaction ends.
        var lockKey = $"{callerId}:{key}";
        return await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({lockKey}, 0)) AS \"Value\"")
            .SingleAsync(cancellationToken);
    }

    public Task<IdempotencyRecord?> FindAsync(string callerId, string key, CancellationToken cancellationToken) =>
        db.Set<IdempotencyRecord>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.CallerId == callerId && r.IdempotencyKey == key, cancellationToken);

    public void Add(IdempotencyRecord record) => db.Add(record);
}
