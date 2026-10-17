namespace NovaWallet.Domain.Ledger;

/// <summary>The stable codes for ledger entry types, used in the database and the HTTP contract alike.</summary>
public static class LedgerEntryTypeCodes
{
    public static string ToCode(LedgerEntryType type) => type switch
    {
        LedgerEntryType.Credit => "CREDIT",
        LedgerEntryType.CreditSettlement => "CREDIT_SETTLEMENT",
        LedgerEntryType.TransferOut => "TRANSFER_OUT",
        LedgerEntryType.TransferIn => "TRANSFER_IN",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static LedgerEntryType FromCode(string code) => code switch
    {
        "CREDIT" => LedgerEntryType.Credit,
        "CREDIT_SETTLEMENT" => LedgerEntryType.CreditSettlement,
        "TRANSFER_OUT" => LedgerEntryType.TransferOut,
        "TRANSFER_IN" => LedgerEntryType.TransferIn,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };
}
