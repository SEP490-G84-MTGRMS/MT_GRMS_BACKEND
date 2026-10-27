using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> builder)
    {
        builder.ToTable("GoodsReceipts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SupplierId).IsRequired();
        builder.Property(x => x.BranchId).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).HasDefaultValue("DRAFT");
        builder.Property(x => x.InvoiceNo).HasMaxLength(100);
        builder.Property(x => x.ReceiverUserId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ConfirmedByUserId).HasMaxLength(50);
        builder.Property(x => x.RejectedByUserId).HasMaxLength(50);
        builder.Property(x => x.RejectionReason).HasMaxLength(500);
        builder.Property(x => x.SubTotal).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.FreightFee).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.DiscountAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.VatPercent).HasColumnType("decimal(5,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.VatAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.GrandTotal).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.PaymentMethod).HasMaxLength(20).HasDefaultValue("AP");
        builder.Property(x => x.CreateApOnConfirm).HasDefaultValue(true);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(1);

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

        builder.HasOne(x => x.PurchaseOrder)
            .WithMany(p => p.GoodsReceipts)
            .HasForeignKey(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReceiverUser)
            .WithMany()
            .HasForeignKey(x => x.ReceiverUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ConfirmedByUser)
            .WithMany()
            .HasForeignKey(x => x.ConfirmedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.RejectedByUser)
            .WithMany()
            .HasForeignKey(x => x.RejectedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
