namespace NovaWallet.Domain.Audit;

/// <summary>One balance mutation, as recorded in the append-only audit trail.</summary>
public sealed class AuditRecord
{
    private AuditRecord()
    {
    }

    public long Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public AuditAction Action { get; private set; }

    public Guid WalletId { get; private set; }

    public Guid TransactionId { get; private set; }

    /// <summary>Signed change to the wallet's balance.</summary>
    public long AmountKobo { get; private set; }

    public long BalanceBeforeKobo { get; private set; }

    public long BalanceAfterKobo { get; private set; }

    public string Actor { get; private set; } = string.Empty;

    public string CorrelationId { get; private set; } = string.Empty;

    public static AuditRecord For(
        AuditAction action,
        Guid walletId,
        Guid transactionId,
        long balanceBeforeKobo,
        long balanceAfterKobo,
        string actor,
        string correlationId,
        DateTimeOffset occurredAt) => new()
        {
            Action = action,
            WalletId = walletId,
            TransactionId = transactionId,
            AmountKobo = checked(balanceAfterKobo - balanceBeforeKobo),
            BalanceBeforeKobo = balanceBeforeKobo,
            BalanceAfterKobo = balanceAfterKobo,
            Actor = actor,
            CorrelationId = correlationId,
            OccurredAt = occurredAt,
        };
}
