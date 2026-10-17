using NovaWallet.Application.Abstractions;
using NovaWallet.Domain.Errors;
using NovaWallet.Domain.Wallets;

namespace NovaWallet.Application.Wallets;

public sealed class WalletService(IWalletStore wallets, ICallerContext caller, TimeProvider clock)
{
    public const string ExistingWalletIdExtension = "existingWalletId";

    public async Task<Result<Wallet>> CreateAsync(string customerId, CancellationToken cancellationToken)
    {
        if (!string.Equals(customerId, caller.Subject, StringComparison.Ordinal))
        {
            return new Error(ErrorCodes.Forbidden, "A customer can only open a wallet for themselves.");
        }

        if (await wallets.FindByCustomerAsync(customerId, cancellationToken) is { } existing)
        {
            return AlreadyExists(existing);
        }

        var wallet = Wallet.OpenFor(customerId, clock.GetUtcNow());
        if (await wallets.TryAddAsync(wallet, cancellationToken))
        {
            return wallet;
        }

        var winner = await wallets.FindByCustomerAsync(customerId, cancellationToken)
            ?? throw new InvalidOperationException("Wallet creation conflicted but no wallet exists.");
        return AlreadyExists(winner);
    }

    public async Task<Result<Wallet>> GetOwnedAsync(Guid walletId, CancellationToken cancellationToken)
    {
        var wallet = await wallets.FindAsync(walletId, cancellationToken);
        return wallet is not null && wallet.IsOwnedBy(caller.Subject)
            ? wallet
            : new Error(ErrorCodes.WalletNotFound, "No wallet with this id belongs to you.");
    }

    private static Error AlreadyExists(Wallet existing) =>
        new Error(ErrorCodes.WalletAlreadyExists, "This customer already has a wallet.")
            .With(ExistingWalletIdExtension, existing.Id);
}
