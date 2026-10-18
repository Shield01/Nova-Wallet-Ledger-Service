using NovaWallet.Application;
using NovaWallet.Domain.Ledger;

namespace NovaWallet.Api.Contracts;

public sealed record StatementEntryResponse(
    long EntryId,
    Guid TransactionId,
    string Type,
    string Direction,
    long AmountKobo,
    long BalanceAfterKobo,
    Guid? CounterpartyWalletId,
    DateTimeOffset CreatedAt)
{
    public const string CreditDirection = "CREDIT";
    public const string DebitDirection = "DEBIT";

    public static StatementEntryResponse From(LedgerEntry entry) => new(
        entry.Id,
        entry.TransactionId,
        LedgerEntryTypeCodes.ToCode(entry.Type),
        entry.AmountKobo > 0 ? CreditDirection : DebitDirection,
        Math.Abs(entry.AmountKobo),
        entry.BalanceAfterKobo,
        entry.CounterpartyWalletId,
        entry.CreatedAt);
}

public sealed record StatementResponse(Guid WalletId, IReadOnlyList<StatementEntryResponse> Items, string? NextCursor)
{
    public static StatementResponse From(Guid walletId, Page<LedgerEntry> page) =>
        new(walletId, page.Items.Select(StatementEntryResponse.From).ToList(), page.Next?.Encode());
}
