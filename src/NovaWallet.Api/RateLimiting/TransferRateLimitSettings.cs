namespace NovaWallet.Api.RateLimiting;

public sealed class TransferRateLimitSettings
{
    public const string SectionName = "RateLimiting:Transfers";

    public int PermitLimit { get; init; } = 10;

    public int WindowSeconds { get; init; } = 60;
}
