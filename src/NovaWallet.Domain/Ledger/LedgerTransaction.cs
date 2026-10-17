namespace NovaWallet.Domain.Ledger;

/// <summary>One money movement. Its entries always sum to zero.</summary>
public sealed class LedgerTransaction
{
    private LedgerTransaction()
    {
    }

    public Guid Id { get; private set; }

    public LedgerTransactionType Type { get; private set; }

    public long AmountKobo { get; private set; }

    public Guid? SourceWalletId { get; private set; }

    public Guid DestinationWalletId { get; private set; }

    public string InitiatedBy { get; private set; } = string.Empty;

    public string CorrelationId { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static LedgerTransaction Create(
        LedgerTransactionType type,
        long amountKobo,
        Guid? sourceWalletId,
        Guid destinationWalletId,
        string initiatedBy,
        string correlationId,
        DateTimeOffset createdAt) => new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            AmountKobo = amountKobo,
            SourceWalletId = sourceWalletId,
            DestinationWalletId = destinationWalletId,
            InitiatedBy = initiatedBy,
            CorrelationId = correlationId,
            CreatedAt = createdAt,
        };
}

public enum LedgerTransactionType
{
    Credit,
    Transfer,
}
