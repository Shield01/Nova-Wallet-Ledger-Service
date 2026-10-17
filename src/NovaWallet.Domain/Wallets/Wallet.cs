namespace NovaWallet.Domain.Wallets;

/// <summary>
/// A balance holder. Customer wallets can't go below zero; the NIP inbound settlement account is
/// the source of every credit, so its balance is the negative of all money credited in.
/// </summary>
public sealed class Wallet
{
    public const string Currency = "NGN";

    /// <summary>The NIP inbound settlement account, seeded by migration.</summary>
    public static readonly Guid SystemAccountId = new("00000000-0000-0000-0000-000000000001");

    public const string SystemAccountName = "NIP_INBOUND_SETTLEMENT";

    private Wallet()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>The opaque customer id; null for the system account.</summary>
    public string? CustomerId { get; private set; }

    public WalletKind Kind { get; private set; }

    public long BalanceKobo { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsSystemAccount => Kind == WalletKind.System;

    public static Wallet OpenFor(string customerId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = customerId,
        Kind = WalletKind.Customer,
        BalanceKobo = 0,
        CreatedAt = now,
    };

    public bool IsOwnedBy(string subject) =>
        Kind == WalletKind.Customer && string.Equals(CustomerId, subject, StringComparison.Ordinal);

    public void ApplyCredit(long amountKobo)
    {
        EnsurePositive(amountKobo);
        BalanceKobo = checked(BalanceKobo + amountKobo);
    }

    /// <summary>Debits the wallet. The caller must have checked funds; this guard is the last line.</summary>
    public void ApplyDebit(long amountKobo)
    {
        EnsurePositive(amountKobo);
        var next = checked(BalanceKobo - amountKobo);
        if (Kind == WalletKind.Customer && next < 0)
        {
            throw new InvalidOperationException("A customer wallet can't go below zero.");
        }

        BalanceKobo = next;
    }

    private static void EnsurePositive(long amountKobo)
    {
        if (amountKobo <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountKobo), "Amounts must be positive.");
        }
    }
}
