using Microsoft.EntityFrameworkCore;
using NovaWallet.Application;
using NovaWallet.Application.Ledger;
using NovaWallet.Domain.Ledger;

namespace NovaWallet.Infrastructure.Persistence.Stores;

public sealed class LedgerStore(LedgerDbContext db) : ILedgerStore
{
    public void Add(LedgerPosting posting)
    {
        db.Add(posting.Transaction);
        db.AddRange(posting.Entries);
    }

    public async Task<long> OutboundTotalAsync(Guid walletId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var debits = await db.Set<LedgerEntry>()
            .Where(e => e.WalletId == walletId && e.Type == LedgerEntryType.TransferOut && e.CreatedAt >= from && e.CreatedAt < to)
            .SumAsync(e => (long?)e.AmountKobo, cancellationToken) ?? 0;
        return checked(-debits);
    }

    public async Task<IReadOnlyList<LedgerEntry>> ListEntriesAsync(Guid walletId, PageCursor? after, int take, CancellationToken cancellationToken)
    {
        var query = db.Set<LedgerEntry>().AsNoTracking().Where(e => e.WalletId == walletId);
        if (after is not null)
        {
            // Row-value comparison (created_at, id) < (@at, @id): seeks the (wallet_id, created_at DESC, id DESC) index.
            query = query.Where(e => EF.Functions.LessThan(ValueTuple.Create(e.CreatedAt, e.Id), ValueTuple.Create(after.At, after.Id)));
        }

        return await query
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}
