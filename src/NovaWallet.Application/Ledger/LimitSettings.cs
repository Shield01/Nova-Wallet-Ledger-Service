namespace NovaWallet.Application.Ledger;

public sealed class LimitSettings
{
    public const string SectionName = "Limits";

    /// <summary>Most a wallet may send per WAT day, in kobo. Default ₦500,000.</summary>
    public long DailyOutboundKobo { get; init; } = 50_000_000;
}
