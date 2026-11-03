using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Catalog;

public class BranchProductConfiguration : IEntityTypeConfiguration<BranchProduct>
{
    public void Configure(EntityTypeBuilder<BranchProduct> builder)
    {
        builder.ToTable("BranchProducts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.BranchId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.SellingPriceOverride).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("Active");
        builder.Property(x => x.ShelfLocation).HasMaxLength(50);

        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.ProductId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
