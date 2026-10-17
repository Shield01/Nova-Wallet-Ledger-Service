namespace NovaWallet.Domain.Audit;

public enum AuditAction
{
    /// <summary>A customer wallet credited by an inbound transfer.</summary>
    Credit,

    /// <summary>The settlement account debited to fund a credit.</summary>
    SystemDebit,

    /// <summary>The source wallet of a transfer.</summary>
    TransferDebit,

    /// <summary>The destination wallet of a transfer.</summary>
    TransferCredit,
}
