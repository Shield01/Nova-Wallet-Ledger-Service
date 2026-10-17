using System.Collections.Concurrent;
using NovaWallet.Application.Outbox;
using NovaWallet.Domain.Outbox;

namespace NovaWallet.IntegrationTests.Infrastructure;

/// <summary>An event publisher that records what it published and fails on demand.</summary>
public sealed class RecordingPublisher : IEventPublisher
{
    public const string FailureMessage = "simulated broker outage";

    public ConcurrentQueue<OutboxMessage> Published { get; } = new();

    public Func<OutboxMessage, bool> FailWhen { get; set; } = _ => false;

    public Func<Exception> Failure { get; set; } = () => new InvalidOperationException(FailureMessage);

    /// <summary>A pause inside each publish, so concurrent dispatchers overlap.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        if (FailWhen(message))
        {
            throw Failure();
        }

        Published.Enqueue(message);
    }
}
