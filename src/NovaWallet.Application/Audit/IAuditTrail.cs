using NovaWallet.Domain.Audit;

namespace NovaWallet.Application.Audit;

public interface IAuditTrail
{
    /// <summary>Appends records in the current unit of work; they persist only if it commits.</summary>
    void Append(params AuditRecord[] records);

    Task<IReadOnlyList<AuditRecord>> ListAsync(Guid? walletId, PageCursor? after, int take, CancellationToken cancellationToken);
}
