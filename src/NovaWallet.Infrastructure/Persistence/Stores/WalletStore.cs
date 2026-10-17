using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.Wallets;
using NovaWallet.Domain.Wallets;

namespace NovaWallet.Infrastructure.Persistence.Stores;

public sealed class WalletStore(LedgerDbContext db) : IWalletStore
{
    public Task<Wallet?> FindAsync(Guid walletId, CancellationToken cancellationToken) =>
        db.Set<Wallet>().AsNoTracking().SingleOrDefaultAsync(w => w.Id == walletId, cancellationToken);

    public Task<Wallet?> FindByCustomerAsync(string customerId, CancellationToken cancellationToken) =>
        db.Set<Wallet>().AsNoTracking().SingleOrDefaultAsync(w => w.CustomerId == customerId, cancellationToken);

    public async Task<IReadOnlyList<Wallet>> LockAsync(IReadOnlyCollection<Guid> walletIds, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Row locks only make sense inside a unit of work.");
        }

        var ids = walletIds.Distinct().ToArray();

        // Ascending id order is what prevents deadlocks between opposite-direction transfers.
        return await db.Set<Wallet>()
            .FromSql($"SELECT * FROM wallets WHERE id = ANY({ids}) ORDER BY id FOR UPDATE")
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryAddAsync(Wallet wallet, CancellationToken cancellationToken)
    {
        db.Add(wallet);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex))
        {
            db.Entry(wallet).State = EntityState.Detached;
            return false;
        }
    }
}
