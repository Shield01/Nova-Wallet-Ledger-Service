using System.Globalization;
using NovaWallet.Application.Abstractions;
using NovaWallet.Application.Audit;
using NovaWallet.Application.Idempotency;
using NovaWallet.Application.Wallets;
using NovaWallet.Domain.Errors;
using NovaWallet.Domain.Ledger;
using NovaWallet.Domain.Wallets;

namespace NovaWallet.Application.Ledger;

/// <summary>Credits a wallet from the NIP inbound settlement account (a simulated inbound transfer).</summary>
public sealed class CreditService(
    IUnitOfWork unitOfWork,
    IdempotencyGuard idempotency,
    IWalletStore wallets,
    ILedgerStore ledger,
    IAuditTrail audit,
    ICallerContext caller,
    ICorrelationContext correlation,
    TimeProvider clock)
{
    public const string Operation = "credit";

    public Task<IdempotentOutcome<CreditResult>> CreditAsync(Guid walletId, long amountKobo, string idempotencyKey, CancellationToken cancellationToken)
    {
        var request = IdempotentRequest.For(
            caller.Subject,
            idempotencyKey,
            Operation,
            ("walletId", walletId.ToString()),
            ("amountKobo", amountKobo.ToString(CultureInfo.InvariantCulture)));

        return unitOfWork.InTransactionAsync(
            ct => idempotency.RunAsync(request, inner => PostAsync(walletId, amountKobo, inner), ct),
            cancellationToken);
    }

    private async Task<Result<CreditResult>> PostAsync(Guid walletId, long amountKobo, CancellationToken cancellationToken)
    {
        var locked = await wallets.LockAsync([walletId, Wallet.SystemAccountId], cancellationToken);
        var now = clock.GetUtcNow();

        var target = locked.SingleOrDefault(w => w.Id == walletId && !w.IsSystemAccount);
        if (target is null)
        {
            return new Error(ErrorCodes.WalletNotFound, "No customer wallet with this id exists.");
        }

        var settlement = locked.Single(w => w.IsSystemAccount);
        var posting = LedgerPosting.Move(LedgerTransactionType.Credit, settlement, target, amountKobo, caller.Subject, correlation.CorrelationId, now);
        ledger.Add(posting);
        audit.Append([.. posting.AuditRecords]);

        return new CreditResult(posting.Transaction.Id, target.Id, amountKobo, target.BalanceKobo, now);
    }
}

public sealed record CreditResult(Guid TransactionId, Guid WalletId, long AmountKobo, long BalanceAfterKobo, DateTimeOffset CreatedAt);
