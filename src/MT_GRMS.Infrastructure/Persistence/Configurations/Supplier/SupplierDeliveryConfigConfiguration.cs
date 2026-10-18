using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class SupplierDeliveryConfigConfiguration : IEntityTypeConfiguration<SupplierDeliveryConfig>
{
    public void Configure(EntityTypeBuilder<SupplierDeliveryConfig> builder)
    {
        builder.ToTable("supplier_delivery_configs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CutoffTime).HasMaxLength(10).HasDefaultValue("17:00");
        builder.Property(x => x.DeliveryDaysJson).HasMaxLength(500).HasDefaultValue("[\"Mon\",\"Tue\",\"Wed\",\"Thu\",\"Fri\",\"Sat\"]");
        builder.Property(x => x.MinOrderValue).HasColumnType("decimal(18,2)").HasDefaultValue(0);
        builder.Property(x => x.UrgentFeePercent).HasColumnType("decimal(5,2)").HasDefaultValue(0);

        builder.HasIndex(x => new { x.TenantId, x.SupplierId }).IsUnique();

        builder.HasMany(x => x.BlackoutDates)
            .WithOne(b => b.SupplierDeliveryConfig)
            .HasForeignKey(b => b.SupplierDeliveryConfigId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
