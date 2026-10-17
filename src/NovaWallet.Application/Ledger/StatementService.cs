using NovaWallet.Application.Wallets;
using NovaWallet.Domain.Errors;
using NovaWallet.Domain.Ledger;

namespace NovaWallet.Application.Ledger;

/// <summary>A customer's own history, read from the ledger entries of a wallet they own.</summary>
public sealed class StatementService(WalletService wallets, ILedgerStore ledger)
{
    public async Task<Result<Page<LedgerEntry>>> GetAsync(Guid walletId, PageCursor? after, int limit, CancellationToken cancellationToken)
    {
        // Not-owned, unknown and the settlement account all look the same to a customer, so ids
        // can't be probed.
        var owned = await wallets.GetOwnedAsync(walletId, cancellationToken);
        if (!owned.IsSuccess)
        {
            return owned.Error;
        }

        var rows = await ledger.ListEntriesAsync(walletId, after, limit + 1, cancellationToken);
        return Page.From(rows, limit, entry => new PageCursor(entry.CreatedAt, entry.Id));
    }
}
