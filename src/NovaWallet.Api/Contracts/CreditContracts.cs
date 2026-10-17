using System.ComponentModel.DataAnnotations;
using NovaWallet.Application.Ledger;

namespace NovaWallet.Api.Contracts;

public sealed class CreditRequest
{
    /// <summary>Amount to credit, in kobo (₦1 = 100 kobo). A JSON integer from 1 to 2^53 − 1.</summary>
    [Range(typeof(long), "1", "9007199254740991", ErrorMessage = "Must be a whole number of kobo from 1 to 9007199254740991.")]
    public long AmountKobo { get; init; }
}

public sealed record CreditResponse(Guid TransactionId, Guid WalletId, string Type, long AmountKobo, long BalanceAfterKobo, DateTimeOffset CreatedAt)
{
    public const string CreditType = "CREDIT";

    public static CreditResponse From(CreditResult result) =>
        new(result.TransactionId, result.WalletId, CreditType, result.AmountKobo, result.BalanceAfterKobo, result.CreatedAt);
}
