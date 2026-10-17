using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaWallet.Domain.Ledger;
using NovaWallet.Domain.Wallets;

namespace NovaWallet.Infrastructure.Persistence.Configurations;

public sealed class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransaction>
{
    public void Configure(EntityTypeBuilder<LedgerTransaction> builder)
    {
        builder.ToTable("ledger_transactions", table => table.HasCheckConstraint("ck_ledger_transactions_amount_positive", "amount_kobo > 0"));
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Type)
            .HasConversion(type => type == LedgerTransactionType.Credit ? "CREDIT" : "TRANSFER", code => code == "CREDIT" ? LedgerTransactionType.Credit : LedgerTransactionType.Transfer)
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(t => t.InitiatedBy).HasMaxLength(64).IsRequired();
        builder.Property(t => t.CorrelationId).HasMaxLength(64).IsRequired();
        builder.HasOne<Wallet>().WithMany().HasForeignKey(t => t.SourceWalletId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Wallet>().WithMany().HasForeignKey(t => t.DestinationWalletId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries", table => table.HasCheckConstraint("ck_ledger_entries_amount_not_zero", "amount_kobo <> 0"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityByDefaultColumn();
        builder.Property(e => e.Type)
            .HasColumnName("entry_type")
            .HasConversion(type => ToCode(type), code => FromCode(code))
            .HasMaxLength(32)
            .IsRequired();
        builder.HasOne<LedgerTransaction>().WithMany().HasForeignKey(e => e.TransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Wallet>().WithMany().HasForeignKey(e => e.WalletId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.WalletId, e.CreatedAt, e.Id }).IsDescending(false, true, true);
    }

    public static string ToCode(LedgerEntryType type) => type switch
    {
        LedgerEntryType.Credit => "CREDIT",
        LedgerEntryType.CreditSettlement => "CREDIT_SETTLEMENT",
        LedgerEntryType.TransferOut => "TRANSFER_OUT",
        LedgerEntryType.TransferIn => "TRANSFER_IN",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static LedgerEntryType FromCode(string code) => code switch
    {
        "CREDIT" => LedgerEntryType.Credit,
        "CREDIT_SETTLEMENT" => LedgerEntryType.CreditSettlement,
        "TRANSFER_OUT" => LedgerEntryType.TransferOut,
        "TRANSFER_IN" => LedgerEntryType.TransferIn,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };
}
