using NovaWallet.Application.Outbox;
using NovaWallet.Domain.Outbox;
using NovaWallet.Infrastructure.Persistence;

namespace NovaWallet.Infrastructure.Outbox;

public sealed class OutboxStore(LedgerDbContext db) : IOutbox
{
    public void Enqueue(OutboxMessage message) => db.Add(message);
}
