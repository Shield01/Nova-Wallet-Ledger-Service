using System.Globalization;
using Microsoft.Extensions.Options;
using NovaWallet.Application.Abstractions;
using NovaWallet.Application.Audit;
using NovaWallet.Application.Idempotency;
using NovaWallet.Application.Wallets;
using NovaWallet.Domain.Errors;
using NovaWallet.Domain.Ledger;

namespace NovaWallet.Application.Ledger;

/// <summary>Moves money between two wallets atomically, never overdrawing the source.</summary>
public sealed class TransferService(
    IUnitOfWork unitOfWork,
    IdempotencyGuard idempotency,
    IWalletStore wallets,
    ILedgerStore ledger,
    IAuditTrail audit,
    ICallerContext caller,
    ICorrelationContext correlation,
    TimeProvider clock,
    IOptions<LimitSettings> limits)
{
    public const string Operation = "transfer";
    public const string DailyLimitExtension = "dailyLimitKobo";
    public const string RemainingExtension = "remainingKobo";

    public Task<IdempotentOutcome<TransferResult>> TransferAsync(
        Guid sourceWalletId,
        Guid destinationWalletId,
        long amountKobo,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var request = IdempotentRequest.For(
            caller.Subject,
            idempotencyKey,
            Operation,
            ("sourceWalletId", sourceWalletId.ToString()),
            ("destinationWalletId", destinationWalletId.ToString()),
            ("amountKobo", amountKobo.ToString(CultureInfo.InvariantCulture)));

        return unitOfWork.InTransactionAsync(
            ct => idempotency.RunAsync(request, inner => PostAsync(sourceWalletId, destinationWalletId, amountKobo, inner), ct),
            cancellationToken);
    }

    // Refusal order follows docs/ARCHITECTURE.md "Transfer refusal precedence".
    private async Task<Result<TransferResult>> PostAsync(Guid sourceWalletId, Guid destinationWalletId, long amountKobo, CancellationToken cancellationToken)
    {
        // Both rows are locked, in ascending id order, before any balance is read.
        var locked = await wallets.LockAsync([sourceWalletId, destinationWalletId], cancellationToken);
        var now = clock.GetStoredUtcNow();

        var source = locked.SingleOrDefault(w => w.Id == sourceWalletId);
        if (source is null || !source.IsOwnedBy(caller.Subject))
        {
            return new Error(ErrorCodes.WalletNotFound, "No wallet with this id belongs to you.");
        }

        if (sourceWalletId == destinationWalletId)
        {
            return new Error(ErrorCodes.SameWalletTransfer, "The source and destination wallets are the same.");
        }

        var destination = locked.SingleOrDefault(w => w.Id == destinationWalletId);
        if (destination is null || destination.IsSystemAccount)
        {
            return new Error(ErrorCodes.DestinationWalletNotFound, "The destination wallet doesn't exist.");
        }

        if (source.BalanceKobo < amountKobo)
        {
            return new Error(ErrorCodes.InsufficientFunds, "The source wallet's balance is lower than the amount.");
        }

        // Checked while the source row is locked, so concurrent transfers can't jointly exceed it.
        var dailyLimit = limits.Value.DailyOutboundKobo;
        var day = WatDay.Containing(now);
        var sentToday = await ledger.OutboundTotalAsync(source.Id, day.Start, day.End, cancellationToken);
        if (checked(sentToday + amountKobo) > dailyLimit)
        {
            return new Error(ErrorCodes.DailyLimitExceeded, "This transfer would exceed the wallet's daily outbound limit (resets at midnight WAT).")
                .With(DailyLimitExtension, dailyLimit)
                .With(RemainingExtension, Math.Max(0, dailyLimit - sentToday));
        }

        var posting = LedgerPosting.Move(LedgerTransactionType.Transfer, source, destination, amountKobo, caller.Subject, correlation.CorrelationId, now);
        ledger.Add(posting);
        audit.Append([.. posting.AuditRecords]);

        return new TransferResult(posting.Transaction.Id, source.Id, destination.Id, amountKobo, source.BalanceKobo, now);
    }
}

public sealed record TransferResult(
    Guid TransferId,
    Guid SourceWalletId,
    Guid DestinationWalletId,
    long AmountKobo,
    long SourceBalanceAfterKobo,
    DateTimeOffset CreatedAt);
