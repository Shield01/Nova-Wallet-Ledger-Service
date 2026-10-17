using NovaWallet.Domain.Audit;
using NovaWallet.Domain.Wallets;

namespace NovaWallet.Domain.Ledger;

/// <summary>Everything one balanced movement writes: the transaction, both entries and both audit records.</summary>
public sealed record LedgerPosting(LedgerTransaction Transaction, IReadOnlyList<LedgerEntry> Entries, IReadOnlyList<AuditRecord> AuditRecords)
{
    /// <summary>
    /// Moves money from <paramref name="from"/> to <paramref name="to"/>, applying both balance
    /// changes. The caller must hold both wallet rows locked and have checked every business rule.
    /// </summary>
    public static LedgerPosting Move(
        LedgerTransactionType type,
        Wallet from,
        Wallet to,
        long amountKobo,
        string actor,
        string correlationId,
        DateTimeOffset now)
    {
        var (debitEntry, creditEntry, debitAction, creditAction) = type switch
        {
            LedgerTransactionType.Credit => (LedgerEntryType.CreditSettlement, LedgerEntryType.Credit, AuditAction.SystemDebit, AuditAction.Credit),
            LedgerTransactionType.Transfer => (LedgerEntryType.TransferOut, LedgerEntryType.TransferIn, AuditAction.TransferDebit, AuditAction.TransferCredit),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };

        var transaction = LedgerTransaction.Create(
            type,
            amountKobo,
            type == LedgerTransactionType.Transfer ? from.Id : null,
            to.Id,
            actor,
            correlationId,
            now);

        var fromBefore = from.BalanceKobo;
        var toBefore = to.BalanceKobo;
        from.ApplyDebit(amountKobo);
        to.ApplyCredit(amountKobo);

        var entries = new[]
        {
            LedgerEntry.Create(transaction.Id, from.Id, debitEntry, checked(-amountKobo), from.BalanceKobo, to.Id, now),
            LedgerEntry.Create(transaction.Id, to.Id, creditEntry, amountKobo, to.BalanceKobo, type == LedgerTransactionType.Transfer ? from.Id : null, now),
        };

        var audit = new[]
        {
            AuditRecord.For(debitAction, from.Id, transaction.Id, fromBefore, from.BalanceKobo, actor, correlationId, now),
            AuditRecord.For(creditAction, to.Id, transaction.Id, toBefore, to.BalanceKobo, actor, correlationId, now),
        };

        return new LedgerPosting(transaction, entries, audit);
    }
}
