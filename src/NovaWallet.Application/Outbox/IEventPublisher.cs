using NovaWallet.Domain.Outbox;

namespace NovaWallet.Application.Outbox;

/// <summary>Delivers one outbox message to whoever listens (a log line in this build; a broker later).</summary>
public interface IEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}
