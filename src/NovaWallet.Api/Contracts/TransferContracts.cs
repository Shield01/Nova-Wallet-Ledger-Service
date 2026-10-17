using System.ComponentModel.DataAnnotations;
using NovaWallet.Application.Ledger;

namespace NovaWallet.Api.Contracts;

public sealed class TransferRequest
{
    /// <summary>A wallet owned by the caller.</summary>
    [Required]
    public Guid? SourceWalletId { get; init; }

    [Required]
    public Guid? DestinationWalletId { get; init; }

    /// <summary>Amount in kobo (₦1 = 100 kobo). A JSON integer from 1 to 2^53 − 1.</summary>
    [Range(typeof(long), "1", "9007199254740991", ErrorMessage = "Must be a whole number of kobo from 1 to 9007199254740991.")]
    public long AmountKobo { get; init; }
}

public sealed record TransferResponse(
    Guid TransferId,
    Guid SourceWalletId,
    Guid DestinationWalletId,
    long AmountKobo,
    long SourceBalanceAfterKobo,
    DateTimeOffset CreatedAt)
{
    public static TransferResponse From(TransferResult result) => new(
        result.TransferId,
        result.SourceWalletId,
        result.DestinationWalletId,
        result.AmountKobo,
        result.SourceBalanceAfterKobo,
        result.CreatedAt);
}
