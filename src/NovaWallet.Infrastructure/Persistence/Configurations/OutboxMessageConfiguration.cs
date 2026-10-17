using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaWallet.Domain.Outbox;

namespace NovaWallet.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", table => table.HasCheckConstraint("ck_outbox_messages_attempts_not_negative", "attempts >= 0"));
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(OutboxMessage.TypeMaxLength).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(OutboxMessage.ErrorMaxLength);

        // The dispatcher only ever looks for unpublished rows, oldest first.
        builder.HasIndex(m => new { m.OccurredAt, m.Id }).HasFilter("published_at IS NULL").HasDatabaseName("ix_outbox_messages_unpublished");
    }
}
