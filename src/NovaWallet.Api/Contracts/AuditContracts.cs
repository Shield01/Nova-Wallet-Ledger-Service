using NovaWallet.Application;
using NovaWallet.Domain.Audit;
using NovaWallet.Infrastructure.Persistence.Configurations;

namespace NovaWallet.Api.Contracts;

public sealed record AuditEntryResponse(
    long Id,
    DateTimeOffset OccurredAt,
    string Action,
    Guid WalletId,
    Guid TransactionId,
    long AmountKobo,
    long BalanceBeforeKobo,
    long BalanceAfterKobo,
    string Actor,
    string CorrelationId);

public sealed record AuditPageResponse(IReadOnlyList<AuditEntryResponse> Items, string? NextCursor)
{
    public static AuditPageResponse From(Page<AuditRecord> page) => new(
        page.Items.Select(a => new AuditEntryResponse(
            a.Id,
            a.OccurredAt,
            AuditRecordConfiguration.ToCode(a.Action),
            a.WalletId,
            a.TransactionId,
            a.AmountKobo,
            a.BalanceBeforeKobo,
            a.BalanceAfterKobo,
            a.Actor,
            a.CorrelationId)).ToList(),
        page.Next?.Encode());
}
