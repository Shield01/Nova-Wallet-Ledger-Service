namespace NovaWallet.Domain.Ledger;

/// <summary>One side of a ledger transaction: a signed change to one wallet's balance.</summary>
public sealed class LedgerEntry
{
    private LedgerEntry()
    {
    }

    public long Id { get; private set; }

    public Guid TransactionId { get; private set; }

    public Guid WalletId { get; private set; }

    public LedgerEntryType Type { get; private set; }

    /// <summary>Positive for money in, negative for money out.</summary>
    public long AmountKobo { get; private set; }

    public long BalanceAfterKobo { get; private set; }

    public Guid? CounterpartyWalletId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static LedgerEntry Create(
        Guid transactionId,
        Guid walletId,
        LedgerEntryType type,
        long amountKobo,
        long balanceAfterKobo,
        Guid? counterpartyWalletId,
        DateTimeOffset createdAt) => new()
        {
            TransactionId = transactionId,
            WalletId = walletId,
            Type = type,
            AmountKobo = amountKobo,
            BalanceAfterKobo = balanceAfterKobo,
            CounterpartyWalletId = counterpartyWalletId,
            CreatedAt = createdAt,
        };
}

public enum LedgerEntryType
{
    /// <summary>Customer side of an inbound credit.</summary>
    Credit,

    /// <summary>Settlement-account side of an inbound credit.</summary>
    CreditSettlement,

    TransferOut,

    TransferIn,
}
