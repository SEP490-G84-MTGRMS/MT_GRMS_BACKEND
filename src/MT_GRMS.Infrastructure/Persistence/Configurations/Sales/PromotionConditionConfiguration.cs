using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Sales;

public class PromotionConditionConfiguration : IEntityTypeConfiguration<PromotionCondition>
{
    public void Configure(EntityTypeBuilder<PromotionCondition> builder)
    {
        builder.ToTable("PromotionConditions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ConditionID");

        builder.Property(x => x.TenantId).HasColumnName("TenantID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.PromotionId).HasColumnName("PromotionID").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("ProductID").IsRequired();
        builder.Property(x => x.ProductRole).HasMaxLength(30).IsRequired().HasDefaultValue("BUY");
        builder.Property(x => x.MinQty).HasColumnType("decimal(12,2)").HasDefaultValue(1.00m);
        builder.Property(x => x.DiscountValue).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Note).HasMaxLength(255);

        builder.HasIndex(x => new { x.TenantId, x.PromotionId, x.ProductId, x.ProductRole }).IsUnique();

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

