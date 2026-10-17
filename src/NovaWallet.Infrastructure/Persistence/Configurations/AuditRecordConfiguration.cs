using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaWallet.Domain;
using NovaWallet.Domain.Audit;
using NovaWallet.Domain.Ledger;
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
            .HasConversion(action => AuditActionCodes.ToCode(action), code => AuditActionCodes.FromCode(code))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(a => a.WalletId).IsRequired();
        builder.Property(a => a.TransactionId).IsRequired();
        builder.Property(a => a.Actor).HasMaxLength(Identifiers.MaxLength).IsRequired();
        builder.Property(a => a.CorrelationId).HasMaxLength(Identifiers.MaxLength).IsRequired();
        builder.HasOne<Wallet>().WithMany().HasForeignKey(a => a.WalletId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LedgerTransaction>().WithMany().HasForeignKey(a => a.TransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.WalletId, a.OccurredAt, a.Id }).IsDescending(false, true, true);
        builder.HasIndex(a => new { a.OccurredAt, a.Id }).IsDescending(true, true);
    }
}
