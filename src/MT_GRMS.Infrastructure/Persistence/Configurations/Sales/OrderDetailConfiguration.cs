using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Sales;

public class OrderDetailConfiguration : IEntityTypeConfiguration<OrderDetail>
{
    public void Configure(EntityTypeBuilder<OrderDetail> builder)
    {
        builder.ToTable("OrderDetails");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("OrderDetailID");

        builder.Property(x => x.OrderId).HasColumnName("OrderID").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("ProductID").IsRequired();
        builder.Property(x => x.PromotionId).HasColumnName("PromotionID");
        builder.Property(x => x.LineNumber).HasDefaultValue(1);
        builder.Property(x => x.Qty).HasColumnType("decimal(12,2)").HasDefaultValue(1.00m);
        builder.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.DiscountAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.ActualPrice).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.IsGift).HasDefaultValue(false);

        // Thuế GTGT từng dòng hàng TT 152/2025/TT-BTC
        builder.Property(x => x.AppliedVatRate).HasColumnType("decimal(5,4)").HasDefaultValue(0.01m);
        builder.Property(x => x.VatAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.VatDiscountAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);

        builder.HasOne(x => x.Order)
            .WithMany(o => o.Details)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Promotion)
            .WithMany()
            .HasForeignKey(x => x.PromotionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
