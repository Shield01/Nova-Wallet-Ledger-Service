using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaWallet.Domain.Audit;
using NovaWallet.Domain.Wallets;

namespace NovaWallet.Infrastructure.Persistence.Configurations;

public sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("audit_log");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityByDefaultColumn();
        builder.Property(a => a.OccurredAt).IsRequired();
        builder.Property(a => a.Action)
            .HasConversion(action => ToCode(action), code => FromCode(code))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(a => a.WalletId).IsRequired();
        builder.Property(a => a.TransactionId).IsRequired();
        builder.Property(a => a.Actor).HasMaxLength(64).IsRequired();
        builder.Property(a => a.CorrelationId).HasMaxLength(64).IsRequired();
        builder.HasOne<Wallet>().WithMany().HasForeignKey(a => a.WalletId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.WalletId, a.OccurredAt, a.Id }).IsDescending(false, true, true);
        builder.HasIndex(a => new { a.OccurredAt, a.Id }).IsDescending(true, true);
    }

    public static string ToCode(AuditAction action) => action switch
    {
        AuditAction.Credit => "CREDIT",
        AuditAction.SystemDebit => "SYSTEM_DEBIT",
        AuditAction.TransferDebit => "TRANSFER_DEBIT",
        AuditAction.TransferCredit => "TRANSFER_CREDIT",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    public static AuditAction FromCode(string code) => code switch
    {
        "CREDIT" => AuditAction.Credit,
        "SYSTEM_DEBIT" => AuditAction.SystemDebit,
        "TRANSFER_DEBIT" => AuditAction.TransferDebit,
        "TRANSFER_CREDIT" => AuditAction.TransferCredit,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };
}
