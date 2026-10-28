using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Inventory;

public class BranchStockConfiguration : IEntityTypeConfiguration<BranchStock>
{
    public void Configure(EntityTypeBuilder<BranchStock> builder)
    {
        builder.ToTable("BranchStocks");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.BranchId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.BatchCode).HasMaxLength(100);
        builder.Property(x => x.QuantityOnHand).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.QuantityReserved).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.LandedCostPerUnit).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.ShelfLocation).HasMaxLength(50);
        builder.Property(x => x.ZoneArea).HasMaxLength(50);
        builder.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(1);

        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.ProductId, x.SupplierId, x.BatchCode, x.ExpiryDate }).IsUnique();

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
