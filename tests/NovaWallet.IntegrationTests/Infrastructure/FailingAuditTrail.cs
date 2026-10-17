using NovaWallet.Application;
using NovaWallet.Application.Audit;
using NovaWallet.Domain.Audit;
using NovaWallet.Infrastructure.Persistence;
using NovaWallet.Infrastructure.Persistence.Stores;

namespace NovaWallet.IntegrationTests.Infrastructure;

/// <summary>
/// Simulates a crash at the worst moment: after the ledger entries and audit records have been
/// written to the database, but before the transaction commits.
/// </summary>
public sealed class FailingAuditTrail(LedgerDbContext db) : IAuditTrail
{
    public const string Message = "Simulated failure after ledger writes, before commit.";

    private readonly AuditTrail _inner = new(db);

    public void Append(params AuditRecord[] records)
    {
        _inner.Append(records);
        db.SaveChanges();
        throw new InvalidOperationException(Message);
    }

    public Task<IReadOnlyList<AuditRecord>> ListAsync(Guid? walletId, PageCursor? after, int take, CancellationToken cancellationToken) =>
        _inner.ListAsync(walletId, after, take, cancellationToken);
}
