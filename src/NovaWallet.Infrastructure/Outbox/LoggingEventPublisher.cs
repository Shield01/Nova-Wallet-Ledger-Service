using System.Text.Json;
using Microsoft.Extensions.Logging;
using NovaWallet.Application.Outbox;
using NovaWallet.Domain.Outbox;

namespace NovaWallet.Infrastructure.Outbox;

/// <summary>The stand-in for a message broker: one structured log line per event.</summary>
public sealed partial class LoggingEventPublisher(ILogger<LoggingEventPublisher> logger) : IEventPublisher
{
    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(message.Payload);
        var transferId = payload.RootElement.TryGetProperty(TransferCompleted.TransferIdProperty, out var value) ? value.GetString() : null;
        LogPublished(logger, message.Type, message.Id, transferId);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 4000, Level = LogLevel.Information, Message = "Published {EventType} {EventId} for transfer {TransferId}")]
    private static partial void LogPublished(ILogger logger, string eventType, Guid eventId, string? transferId);
}
