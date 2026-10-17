namespace NovaWallet.Domain.Ledger;

public static class Money
{
    /// <summary>2^53 − 1: the largest integer every JSON client carries exactly.</summary>
    public const long MaxAmountKobo = 9_007_199_254_740_991;

    public static bool IsValidAmount(long amountKobo) => amountKobo is > 0 and <= MaxAmountKobo;
}
