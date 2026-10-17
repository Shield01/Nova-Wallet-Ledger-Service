using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaWallet.Domain;
using NovaWallet.Domain.Wallets;

namespace NovaWallet.Infrastructure.Persistence.Configurations;

public sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("wallets", table =>
        {
            table.HasCheckConstraint("ck_wallets_customer_balance_not_negative", "kind = 'SYSTEM' OR balance_kobo >= 0");
            table.HasCheckConstraint(
                "ck_wallets_kind_matches_customer",
                "(kind = 'SYSTEM' AND customer_id IS NULL) OR (kind = 'CUSTOMER' AND customer_id IS NOT NULL)");
        });
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.Property(w => w.CustomerId).HasMaxLength(Identifiers.MaxLength);
        builder.HasIndex(w => w.CustomerId).IsUnique();
        builder.Property(w => w.Kind)
            .HasConversion(kind => kind == WalletKind.System ? "SYSTEM" : "CUSTOMER", value => value == "SYSTEM" ? WalletKind.System : WalletKind.Customer)
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(w => w.BalanceKobo).IsRequired();
        builder.Property(w => w.CreatedAt).IsRequired();
        builder.Ignore(w => w.IsSystemAccount);
    }
}
