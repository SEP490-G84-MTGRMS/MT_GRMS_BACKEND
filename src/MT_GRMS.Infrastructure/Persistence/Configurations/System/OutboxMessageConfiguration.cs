using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.System;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.System;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("text").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).IsRequired();
        builder.Property(x => x.LockedBy).HasMaxLength(100);
        builder.Property(x => x.LastError).HasMaxLength(2000);

        builder.HasIndex(x => new { x.TenantId, x.Status, x.NextAttemptAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
    }
}
