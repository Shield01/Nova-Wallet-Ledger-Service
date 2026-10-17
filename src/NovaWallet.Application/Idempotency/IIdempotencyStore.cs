using NovaWallet.Domain.Idempotency;

namespace NovaWallet.Application.Idempotency;

/// <summary>Persistence for idempotency records. Every method joins the current unit of work.</summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Takes a transaction-scoped lock on the caller and key without waiting. False means another
    /// transaction holds it: the same request is still being processed.
    /// </summary>
    Task<bool> TryLockAsync(string callerId, string key, CancellationToken cancellationToken);

    Task<IdempotencyRecord?> FindAsync(string callerId, string key, CancellationToken cancellationToken);

    void Add(IdempotencyRecord record);
}
