using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Catalog;

public class BranchProductUnitPriceConfiguration : IEntityTypeConfiguration<BranchProductUnitPrice>
{
    public void Configure(EntityTypeBuilder<BranchProductUnitPrice> builder)
    {
        builder.ToTable("BranchProductUnitPrices");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.BranchProductId).IsRequired();
        builder.Property(x => x.ProductUnitId).IsRequired();
        builder.Property(x => x.SellingPriceOverride).HasColumnType("decimal(18,2)").IsRequired();

        builder.HasIndex(x => new { x.BranchProductId, x.ProductUnitId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne(x => x.BranchProduct)
            .WithMany(bp => bp.UnitPriceOverrides)
            .HasForeignKey(x => x.BranchProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ProductUnit)
            .WithMany()
            .HasForeignKey(x => x.ProductUnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
