using NovaWallet.Domain.Outbox;

namespace NovaWallet.Application.Outbox;

/// <summary>Records an event in the current unit of work, so it commits or rolls back with it.</summary>
public interface IOutbox
{
    void Enqueue(OutboxMessage message);
}
