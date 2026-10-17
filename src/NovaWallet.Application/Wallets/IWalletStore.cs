using NovaWallet.Domain.Wallets;

namespace NovaWallet.Application.Wallets;

public interface IWalletStore
{
    Task<Wallet?> FindAsync(Guid walletId, CancellationToken cancellationToken);

    Task<Wallet?> FindByCustomerAsync(string customerId, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the given wallets (SELECT … FOR UPDATE) in ascending id order, inside the current unit
    /// of work, and returns them tracked so balance changes are saved on commit. Missing ids are
    /// simply absent from the result.
    /// </summary>
    Task<IReadOnlyList<Wallet>> LockAsync(IReadOnlyCollection<Guid> walletIds, CancellationToken cancellationToken);

    /// <summary>Adds the wallet; returns false if the customer already has one (lost a race).</summary>
    Task<bool> TryAddAsync(Wallet wallet, CancellationToken cancellationToken);
}
