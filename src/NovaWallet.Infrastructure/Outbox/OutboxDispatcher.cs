using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NovaWallet.Application.Abstractions;
using NovaWallet.Application.Outbox;
using NovaWallet.Domain.Outbox;
using NovaWallet.Infrastructure.Persistence;

namespace NovaWallet.Infrastructure.Outbox;

/// <summary>
/// One poll: claims a batch of unpublished messages with FOR UPDATE SKIP LOCKED, so several
/// dispatchers (or instances) never publish the same row concurrently, publishes each through the
/// port and records the result, all in one transaction.
/// </summary>
public sealed partial class OutboxDispatcher(
    LedgerDbContext db,
    IUnitOfWork unitOfWork,
    IEventPublisher publisher,
    TimeProvider clock,
    IOptions<OutboxSettings> settings,
    ILogger<OutboxDispatcher> logger)
{
    /// <summary>How many messages the poll claimed and how many of those it published.</summary>
    public Task<OutboxPoll> DispatchOnceAsync(CancellationToken cancellationToken) =>
        unitOfWork.InTransactionAsync(
            async ct =>
            {
                var maxAttempts = settings.Value.MaxAttempts;
                var batchSize = settings.Value.BatchSize;
                var batch = await db.Set<OutboxMessage>()
                    .FromSql($"""
                        SELECT * FROM outbox_messages
                        WHERE published_at IS NULL AND attempts < {maxAttempts}
                        ORDER BY occurred_at, id
                        LIMIT {batchSize}
                        FOR UPDATE SKIP LOCKED
                        """)
                    .ToListAsync(ct);

                var published = 0;
                foreach (var message in batch)
                {
                    try
                    {
                        await publisher.PublishAsync(message, ct);
                        message.MarkPublished(clock.GetStoredUtcNow());
                        published++;
                    }
                    catch (Exception exception) when (!ct.IsCancellationRequested)
                    {
                        // Only shutdown propagates. A publisher's own timeout (often an
                        // OperationCanceledException) counts as a failed attempt like any other.
                        message.RecordFailure($"{exception.GetType().Name}: {exception.Message}");
                        if (message.Attempts >= maxAttempts)
                        {
                            LogGaveUp(logger, message.Type, message.Id, message.Attempts, exception);
                        }
                        else
                        {
                            LogWillRetry(logger, message.Type, message.Id, message.Attempts, exception);
                        }
                    }
                }

                return new OutboxPoll(batch.Count, published);
            },
            cancellationToken);

    public readonly record struct OutboxPoll(int Claimed, int Published);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "Publishing {EventType} {EventId} failed (attempt {Attempts}); it will be retried")]
    private static partial void LogWillRetry(ILogger logger, string eventType, Guid eventId, int attempts, Exception exception);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Error, Message = "Publishing {EventType} {EventId} failed {Attempts} times; giving up, the message stays unpublished")]
    private static partial void LogGaveUp(ILogger logger, string eventType, Guid eventId, int attempts, Exception exception);
}
