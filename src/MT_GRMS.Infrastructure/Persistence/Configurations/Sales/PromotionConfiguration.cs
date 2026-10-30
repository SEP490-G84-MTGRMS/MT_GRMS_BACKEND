using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Sales;

public class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        builder.ToTable("Promotions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("PromotionID");

        builder.Property(x => x.TenantId).HasColumnName("TenantID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.PromotionCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PromotionName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DiscountValue).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.MinOrderValue).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.MaxDiscountAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.UsageLimit);
        builder.Property(x => x.UsedCount).HasDefaultValue(0);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.TargetBranches).HasMaxLength(255).HasDefaultValue("ALL");
        builder.Property(x => x.BudgetLimit).HasColumnType("decimal(18,2)");
        builder.Property(x => x.TotalDiscountGiven).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.VoucherPrefix).HasMaxLength(20);
        builder.Property(x => x.TotalVoucherQuantity).HasDefaultValue(0);

        builder.HasIndex(x => new { x.TenantId, x.PromotionCode }).IsUnique();

        // 1 - N với Conditions
        builder.HasMany(x => x.Conditions)
            .WithOne(c => c.Promotion)
            .HasForeignKey(c => c.PromotionId)
            .OnDelete(DeleteBehavior.Cascade);

        // 1 - N với VoucherCodes
        builder.HasMany(x => x.VoucherCodes)
            .WithOne(v => v.Promotion)
            .HasForeignKey(v => v.PromotionId)
            .OnDelete(DeleteBehavior.Cascade);

        // 1 - N với OrderDetails
        builder.HasMany(x => x.OrderDetails)
            .WithOne(o => o.Promotion)
            .HasForeignKey(o => o.PromotionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
