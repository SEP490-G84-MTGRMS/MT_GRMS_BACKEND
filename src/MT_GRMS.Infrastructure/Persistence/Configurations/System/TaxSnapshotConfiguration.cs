using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.System;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.System;

public class TaxSnapshotConfiguration : IEntityTypeConfiguration<TaxSnapshot>
{
    public void Configure(EntityTypeBuilder<TaxSnapshot> builder)
    {
        builder.ToTable("TaxSnapshots");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("TaxID");

        builder.Property(x => x.TenantId).HasColumnName("TenantID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.TotalRevenue).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.TaxRateVat).HasColumnType("decimal(5,2)");
        builder.Property(x => x.VatAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.TaxRatePit).HasColumnType("decimal(5,2)");
        builder.Property(x => x.PitPayable).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.VatMethod).HasMaxLength(50).HasDefaultValue("DIRECT");
        builder.Property(x => x.PitMethod).HasMaxLength(50).HasDefaultValue("PROGRESSIVE");
        builder.Property(x => x.Status).HasMaxLength(30).HasDefaultValue("DRAFT");
        builder.Property(x => x.IsLocked).HasDefaultValue(false);

        builder.HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
