using NovaWallet.Domain.Ledger;

namespace NovaWallet.Application.Ledger;

public interface ILedgerStore
{
    /// <summary>Adds a posting's transaction and entries to the current unit of work.</summary>
    void Add(LedgerPosting posting);

    /// <summary>Total sent from the wallet by transfers in [from, to), in kobo (a positive number).</summary>
    Task<long> OutboundTotalAsync(Guid walletId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
