using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaWallet.Application.Idempotency;
using NovaWallet.Domain;
using NovaWallet.Domain.Idempotency;

namespace NovaWallet.Infrastructure.Persistence.Configurations;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");
        builder.HasKey(r => new { r.CallerId, r.IdempotencyKey });
        builder.Property(r => r.CallerId).HasMaxLength(Identifiers.MaxLength);
        builder.Property(r => r.IdempotencyKey).HasMaxLength(IdempotentRequest.MaxKeyLength);
        builder.Property(r => r.RequestHash).HasMaxLength(Identifiers.Sha256HexLength).IsFixedLength().IsRequired();
        builder.Property(r => r.Outcome).HasMaxLength(64).IsRequired();
        builder.Property(r => r.ResponseBody).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();
    }
}
