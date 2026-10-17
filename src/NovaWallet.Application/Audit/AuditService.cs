using NovaWallet.Application.Wallets;
using NovaWallet.Domain.Audit;
using NovaWallet.Domain.Errors;

namespace NovaWallet.Application.Audit;

public sealed class AuditService(IAuditTrail trail, IWalletStore wallets)
{
    public async Task<Result<Page<AuditRecord>>> ListAsync(Guid? walletId, PageCursor? after, int limit, CancellationToken cancellationToken)
    {
        // Auditors may inspect any wallet, the settlement account included, but an unknown id is
        // reported rather than silently returning an empty page.
        if (walletId is { } id && await wallets.FindAsync(id, cancellationToken) is null)
        {
            return new Error(ErrorCodes.WalletNotFound, "No wallet with this id exists.");
        }

        var rows = await trail.ListAsync(walletId, after, limit + 1, cancellationToken);
        return Page.From(rows, limit, record => new PageCursor(record.OccurredAt, record.Id));
    }
}
