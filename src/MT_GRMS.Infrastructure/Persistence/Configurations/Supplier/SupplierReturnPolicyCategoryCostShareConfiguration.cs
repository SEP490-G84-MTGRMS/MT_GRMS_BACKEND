using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class SupplierReturnPolicyCategoryCostShareConfiguration
    : IEntityTypeConfiguration<SupplierReturnPolicyCategoryCostShare>
{
    public void Configure(EntityTypeBuilder<SupplierReturnPolicyCategoryCostShare> builder)
    {
        builder.ToTable("SupplierReturnPolicyCategoryCostShares");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.StoreCostSharePercent).HasColumnType("decimal(5,2)");
        builder.Property(x => x.SupplierCostSharePercent).HasColumnType("decimal(5,2)");

        // Soft-delete: unique chỉ trên dòng còn sống để upsert soft-delete + add lại cùng Category.
        builder.HasIndex(x => new { x.TenantId, x.PolicyId, x.CategoryId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = FALSE");

        builder.HasOne(x => x.Policy)
            .WithMany(p => p.CategoryCostShares)
            .HasForeignKey(x => x.PolicyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
