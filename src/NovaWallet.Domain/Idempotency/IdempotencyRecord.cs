namespace NovaWallet.Domain.Idempotency;

/// <summary>The stored final outcome of a request made with an Idempotency-Key.</summary>
public sealed class IdempotencyRecord
{
    public const string SuccessOutcome = "SUCCESS";

    private IdempotencyRecord()
    {
    }

    public string CallerId { get; private set; } = string.Empty;

    public string IdempotencyKey { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    /// <summary><see cref="SuccessOutcome"/> or the refusal's error code.</summary>
    public string Outcome { get; private set; } = string.Empty;

    /// <summary>The use-case result, or the refusal's detail and extensions, as JSON.</summary>
    public string ResponseBody { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static IdempotencyRecord Create(string callerId, string key, string requestHash, string outcome, string responseBody, DateTimeOffset createdAt) => new()
    {
        CallerId = callerId,
        IdempotencyKey = key,
        RequestHash = requestHash,
        Outcome = outcome,
        ResponseBody = responseBody,
        CreatedAt = createdAt,
    };
}
