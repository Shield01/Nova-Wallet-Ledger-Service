namespace NovaWallet.Domain.Outbox;

/// <summary>An event recorded in the same transaction as the change it describes, published later.</summary>
public sealed class OutboxMessage
{
    public const int TypeMaxLength = 64;
    public const int ErrorMaxLength = 1000;

    private OutboxMessage()
    {
    }

    /// <summary>The event id; consumers de-duplicate on it.</summary>
    public Guid Id { get; private set; }

    public string Type { get; private set; } = string.Empty;

    /// <summary>The event as JSON.</summary>
    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Failed publish attempts; a successful publish doesn't count.</summary>
    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage Create(Guid eventId, string type, string payload, DateTimeOffset occurredAt) => new()
    {
        Id = eventId,
        Type = type,
        Payload = payload,
        OccurredAt = occurredAt,
    };

    public void MarkPublished(DateTimeOffset at) => PublishedAt = at;

    public void RecordFailure(string error)
    {
        Attempts = checked(Attempts + 1);
        LastError = error.Length > ErrorMaxLength ? error[..ErrorMaxLength] : error;
    }
}
