using System.ComponentModel.DataAnnotations;
using NovaWallet.Domain.Wallets;

namespace NovaWallet.Api.Contracts;

public sealed class CreateWalletRequest
{
    public const string CustomerIdPattern = "^[A-Za-z0-9_-]{1,64}$";

    /// <summary>The opaque customer id. Must equal the caller's token subject.</summary>
    [Required]
    [RegularExpression(CustomerIdPattern, ErrorMessage = "Must be 1-64 characters of letters, digits, '_' or '-'.")]
    public string? CustomerId { get; init; }
}

public sealed record BalanceResponse(Guid WalletId, long BalanceKobo, string Currency, DateTimeOffset AsOf);

public sealed record WalletResponse(Guid WalletId, string CustomerId, string Currency, long BalanceKobo, DateTimeOffset CreatedAt)
{
    // Only customer wallets are ever returned to clients, and those always have a customer id.
    public static WalletResponse From(Wallet wallet) =>
        new(wallet.Id, wallet.CustomerId!, Wallet.Currency, wallet.BalanceKobo, wallet.CreatedAt);
}
