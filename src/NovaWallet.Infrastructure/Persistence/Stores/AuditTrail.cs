using Microsoft.EntityFrameworkCore;
using NovaWallet.Application;
using NovaWallet.Application.Audit;
using NovaWallet.Domain.Audit;

namespace NovaWallet.Infrastructure.Persistence.Stores;

public sealed class AuditTrail(LedgerDbContext db) : IAuditTrail
{
    public void Append(params AuditRecord[] records) => db.AddRange(records);

    public async Task<IReadOnlyList<AuditRecord>> ListAsync(Guid? walletId, PageCursor? after, int take, CancellationToken cancellationToken)
    {
        var query = db.Set<AuditRecord>().AsNoTracking();
        if (walletId is { } id)
        {
            query = query.Where(a => a.WalletId == id);
        }

        if (after is not null)
        {
            // A row-value comparison, (occurred_at, id) < (@at, @id), which the index can seek to.
            query = query.Where(a => EF.Functions.LessThan(ValueTuple.Create(a.OccurredAt, a.Id), ValueTuple.Create(after.At, after.Id)));
        }

        return await query
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}
