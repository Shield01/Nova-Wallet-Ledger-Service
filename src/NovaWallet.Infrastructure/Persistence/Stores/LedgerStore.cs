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
}
