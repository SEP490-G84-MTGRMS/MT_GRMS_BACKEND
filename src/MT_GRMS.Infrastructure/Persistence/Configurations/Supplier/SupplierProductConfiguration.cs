using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class SupplierProductConfiguration : IEntityTypeConfiguration<SupplierProduct>
{
    public void Configure(EntityTypeBuilder<SupplierProduct> builder)
    {
        builder.ToTable("SupplierProducts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SupplierId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.SupplierProductCode).HasMaxLength(100);
        builder.Property(x => x.DeliveryUoM).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AgreedWholesalePrice).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.MinimumOrderQuantity).HasColumnType("decimal(18,3)").HasDefaultValue(1);
        builder.Property(x => x.ConversionRate).HasColumnType("decimal(18,3)").HasDefaultValue(1.000m);
        builder.Property(x => x.IsPrimarySupplier).HasDefaultValue(false);
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(1);

        // Unique Index (TenantId, SupplierId, ProductId) - BR-SP01
        builder.HasIndex(x => new { x.TenantId, x.SupplierId, x.ProductId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.ProductId, x.IsPrimarySupplier });

        // Relationships
        builder.HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.DeliveryUnit)
            .WithMany()
            .HasForeignKey(x => x.DeliveryUomId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
