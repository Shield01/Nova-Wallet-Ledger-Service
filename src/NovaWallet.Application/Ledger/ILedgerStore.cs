using NovaWallet.Domain.Ledger;

namespace NovaWallet.Application.Ledger;

public interface ILedgerStore
{
    /// <summary>Adds a posting's transaction and entries to the current unit of work.</summary>
    void Add(LedgerPosting posting);
}
