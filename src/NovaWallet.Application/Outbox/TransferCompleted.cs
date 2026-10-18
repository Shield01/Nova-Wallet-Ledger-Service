using System.Text.Json;
using NovaWallet.Domain.Outbox;

namespace NovaWallet.Application.Outbox;

/// <summary>Published once a transfer has committed. Delivery is at least once; de-duplicate on <see cref="EventId"/>.</summary>
public sealed record TransferCompleted(
    Guid EventId,
    Guid TransferId,
    Guid SourceWalletId,
    Guid DestinationWalletId,
    long AmountKobo,
    DateTimeOffset OccurredAt,
    string CorrelationId)
{
    public const string EventType = "TransferCompleted";

    /// <summary>The payload property holding the transfer id (camelCase, as serialised).</summary>
    public const string TransferIdProperty = "transferId";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public OutboxMessage ToMessage() =>
        OutboxMessage.Create(EventId, EventType, JsonSerializer.Serialize(this, Json), OccurredAt);
}
