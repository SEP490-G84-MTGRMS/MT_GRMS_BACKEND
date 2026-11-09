using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Catalog;

public class ProductPriceHistoryConfiguration : IEntityTypeConfiguration<ProductPriceHistory>
{
    public void Configure(EntityTypeBuilder<ProductPriceHistory> builder)
    {
        builder.ToTable("ProductPriceHistories");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.BranchId);
        builder.Property(x => x.ProductUnitId);

        builder.Property(x => x.PriceScope).HasMaxLength(20).IsRequired().HasDefaultValue("MASTER");
        builder.Property(x => x.PriceType).HasMaxLength(20).IsRequired().HasDefaultValue("SELLING_PRICE");

        builder.Property(x => x.OldPrice).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.NewPrice).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.CostPriceSnapshot).HasColumnType("decimal(18,2)");
        builder.Property(x => x.IsBelowCost).IsRequired().HasDefaultValue(false);

        builder.Property(x => x.ManagerApprovedBy).HasMaxLength(100);
        builder.Property(x => x.ManagerApprovalReason).HasMaxLength(255);

        builder.Property(x => x.ChangedByUserId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ChangedByUserName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(255).IsRequired().HasDefaultValue("Điều chỉnh giá bán");

        builder.HasIndex(x => new { x.TenantId, x.ProductId, x.CreatedAt });
        builder.HasIndex(x => new { x.TenantId, x.BranchId });

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

