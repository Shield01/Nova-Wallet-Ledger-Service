using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NovaWallet.Domain.Idempotency;

namespace NovaWallet.IntegrationTests.Infrastructure;

/// <summary>
/// Fails the save that would write an idempotency record: the moment after the operation's writes
/// are pending and before the transaction commits. If the record and the writes didn't share one
/// transaction, the writes would survive this.
/// </summary>
public sealed class FailOnIdempotencyRecordInterceptor : SaveChangesInterceptor
{
    public const string Message = "Simulated failure while saving the idempotency record.";

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<IdempotencyRecord>().Any(e => e.State == EntityState.Added))
        {
            throw new InvalidOperationException(Message);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
