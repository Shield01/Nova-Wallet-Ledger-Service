using NovaWallet.Domain.Ledger;
using NovaWallet.Infrastructure.Persistence;

namespace NovaWallet.IntegrationTests.Infrastructure;

/// <summary>For component tests that need an audit row: the audit trail references a real ledger transaction.</summary>
internal static class TestLedger
{
    public static Guid AddTransaction(LedgerDbContext db, Guid walletId, long amountKobo, string actor, string correlationId, DateTimeOffset now)
    {
        var transaction = LedgerTransaction.Create(LedgerTransactionType.Credit, amountKobo, null, walletId, actor, correlationId, now);
        db.Add(transaction);
        return transaction.Id;
    }
}
