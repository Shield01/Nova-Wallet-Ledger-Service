using Microsoft.EntityFrameworkCore;
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
}
