namespace NovaWallet.Application.Abstractions;

/// <summary>
/// Runs a use case in one database transaction. Every port used inside <paramref name="work"/>
/// joins it. The transaction commits when the work returns and rolls back if it throws.
/// </summary>
public interface IUnitOfWork
{
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}
