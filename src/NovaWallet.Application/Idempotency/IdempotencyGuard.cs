using System.Text.Json;
using NovaWallet.Domain.Errors;
using NovaWallet.Domain.Idempotency;

namespace NovaWallet.Application.Idempotency;

/// <summary>
/// Runs an operation at most once per caller and key. Must be called inside a unit of work: the
/// lock, the operation's writes and the stored outcome all share its transaction.
/// </summary>
public sealed class IdempotencyGuard(IIdempotencyStore store, TimeProvider clock)
{
    /// <summary>Refusals that are final for the request, so a retry with the same key gets them again.</summary>
    public static readonly IReadOnlySet<string> StoredRefusals = new HashSet<string>(StringComparer.Ordinal)
    {
        ErrorCodes.InsufficientFunds,
        ErrorCodes.DailyLimitExceeded,
        ErrorCodes.SameWalletTransfer,
        ErrorCodes.DestinationWalletNotFound,
        ErrorCodes.WalletNotFound,
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IdempotentOutcome<T>> RunAsync<T>(
        IdempotentRequest request,
        Func<CancellationToken, Task<Result<T>>> operation,
        CancellationToken cancellationToken)
    {
        if (!await store.TryLockAsync(request.CallerId, request.Key, cancellationToken))
        {
            return IdempotentOutcome<T>.Fresh(new Error(
                ErrorCodes.IdempotencyRequestInProgress,
                "A request with this Idempotency-Key is still being processed. Retry with the same key shortly."));
        }

        if (await store.FindAsync(request.CallerId, request.Key, cancellationToken) is { } stored)
        {
            return stored.RequestHash == request.RequestHash
                ? IdempotentOutcome<T>.Replay(Restore<T>(stored))
                : IdempotentOutcome<T>.Fresh(new Error(
                    ErrorCodes.IdempotencyKeyReused,
                    "This Idempotency-Key was already used for a different request. Use a new key for a new payment."));
        }

        var result = await operation(cancellationToken);
        if (result.IsSuccess)
        {
            store.Add(Record(request, IdempotencyRecord.SuccessOutcome, JsonSerializer.Serialize(result.Value, Json)));
        }
        else if (StoredRefusals.Contains(result.Error.Code))
        {
            var refusal = new StoredRefusal(result.Error.Detail, result.Error.Extensions);
            store.Add(Record(request, result.Error.Code, JsonSerializer.Serialize(refusal, Json)));
        }

        return IdempotentOutcome<T>.Fresh(result);
    }

    private IdempotencyRecord Record(IdempotentRequest request, string outcome, string body) =>
        IdempotencyRecord.Create(request.CallerId, request.Key, request.RequestHash, outcome, body, clock.GetUtcNow());

    private static Result<T> Restore<T>(IdempotencyRecord stored)
    {
        if (stored.Outcome == IdempotencyRecord.SuccessOutcome)
        {
            return JsonSerializer.Deserialize<T>(stored.ResponseBody, Json)!;
        }

        var refusal = JsonSerializer.Deserialize<StoredRefusal>(stored.ResponseBody, Json)!;
        return new Error(stored.Outcome, refusal.Detail) { Extensions = refusal.Extensions ?? new Dictionary<string, object?>() };
    }

    private sealed record StoredRefusal(string Detail, IReadOnlyDictionary<string, object?>? Extensions);
}

/// <summary>The result of an idempotent call, and whether it was replayed from a stored outcome.</summary>
public sealed record IdempotentOutcome<T>(Result<T> Result, bool Replayed)
{
    public static IdempotentOutcome<T> Fresh(Result<T> result) => new(result, false);

    public static IdempotentOutcome<T> Replay(Result<T> result) => new(result, true);
}
