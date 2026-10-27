using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> builder)
    {
        builder.ToTable("GoodsReceiptLines");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.GoodsReceiptId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.OrderedQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.DeliveredQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.AcceptedQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.RejectedQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.LineAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.BatchCode).HasMaxLength(100);
        builder.Property(x => x.FreightAllocated).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.DiscountAllocated).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.VatAllocated).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.LandedUnitCost).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.RemainOption).HasMaxLength(20);
        builder.Property(x => x.QualityStatus).HasMaxLength(20).HasDefaultValue("ACCEPTED");
        builder.Property(x => x.RejectionReason).HasMaxLength(300);
        builder.Property(x => x.Note).HasMaxLength(300);

        builder.HasOne(x => x.GoodsReceipt)
            .WithMany(g => g.Lines)
            .HasForeignKey(x => x.GoodsReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PurchaseOrderDetail)
            .WithMany()
            .HasForeignKey(x => x.PurchaseOrderDetailId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.BranchStock)
            .WithMany()
            .HasForeignKey(x => x.BranchStockId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
