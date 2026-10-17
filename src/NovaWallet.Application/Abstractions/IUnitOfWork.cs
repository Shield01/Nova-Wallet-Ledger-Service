namespace NovaWallet.Application.Abstractions;

/// <summary>
/// Runs a use case in one database transaction. Every port used inside the work joins it. The
/// transaction commits when the work returns and rolls back if it throws.
/// </summary>
public interface IUnitOfWork
{
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a point inside the current transaction that <see cref="ISavepoint.RollbackAsync"/>
    /// returns to, discarding every write and tracked change made after it.
    /// </summary>
    Task<ISavepoint> SavepointAsync(CancellationToken cancellationToken);
}

public interface ISavepoint
{
    Task RollbackAsync(CancellationToken cancellationToken);
}
