using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class SupplierProductConfiguration : IEntityTypeConfiguration<SupplierProduct>
{
    public void Configure(EntityTypeBuilder<SupplierProduct> builder)
    {
        builder.ToTable("supplier_products");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SupplierId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.SupplierProductCode).HasMaxLength(50);
        builder.Property(x => x.DeliveryUoM).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AgreedWholesalePrice).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.MinimumOrderQuantity).HasDefaultValue(1).IsRequired();
        builder.Property(x => x.IsPrimarySupplier).HasDefaultValue(false).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();

        // Unique Index (TenantId, SupplierId, ProductId) - BR-SP01
        builder.HasIndex(x => new { x.TenantId, x.SupplierId, x.ProductId }).IsUnique();

        // Index for Primary Supplier queries & toggling - BR-SP02
        builder.HasIndex(x => new { x.TenantId, x.ProductId, x.IsPrimarySupplier });

        // Relationships
        builder.HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
