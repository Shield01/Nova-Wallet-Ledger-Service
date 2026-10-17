namespace NovaWallet.Domain.Audit;

/// <summary>The stable codes for audit actions, used in the database and the HTTP contract alike.</summary>
public static class AuditActionCodes
{
    public static string ToCode(AuditAction action) => action switch
    {
        AuditAction.Credit => "CREDIT",
        AuditAction.SystemDebit => "SYSTEM_DEBIT",
        AuditAction.TransferDebit => "TRANSFER_DEBIT",
        AuditAction.TransferCredit => "TRANSFER_CREDIT",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    public static AuditAction FromCode(string code) => code switch
    {
        "CREDIT" => AuditAction.Credit,
        "SYSTEM_DEBIT" => AuditAction.SystemDebit,
        "TRANSFER_DEBIT" => AuditAction.TransferDebit,
        "TRANSFER_CREDIT" => AuditAction.TransferCredit,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };
}
