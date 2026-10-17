using NovaWallet.Domain.Wallets;

namespace NovaWallet.Application.Wallets;

public interface IWalletStore
{
    Task<Wallet?> FindAsync(Guid walletId, CancellationToken cancellationToken);

    Task<Wallet?> FindByCustomerAsync(string customerId, CancellationToken cancellationToken);

    /// <summary>Adds the wallet; returns false if the customer already has one (lost a race).</summary>
    Task<bool> TryAddAsync(Wallet wallet, CancellationToken cancellationToken);
}
