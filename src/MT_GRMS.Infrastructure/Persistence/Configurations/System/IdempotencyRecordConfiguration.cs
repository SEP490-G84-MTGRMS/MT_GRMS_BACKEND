using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.System;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.System;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");
        builder.Property(i => i.Key).HasMaxLength(100).IsRequired();
        builder.Property(i => i.Endpoint).HasMaxLength(200).IsRequired();
        builder.Property(i => i.PayloadHash).HasMaxLength(128).IsRequired();
        builder.Property(i => i.Status).HasMaxLength(20).IsRequired().HasDefaultValue("PENDING");
        builder.HasIndex(i => new { i.TenantId, i.Endpoint, i.Key }).IsUnique();
    }
}
