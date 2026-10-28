using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class PurchaseOrderDetailConfiguration : IEntityTypeConfiguration<PurchaseOrderDetail>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderDetail> builder)
    {
        builder.ToTable("PurchaseOrderDetails");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PurchaseOrderId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.EmployeeQty).HasColumnType("decimal(18,3)");
        builder.Property(x => x.StoreQty).HasColumnType("decimal(18,3)");
        builder.Property(x => x.FinalQty).HasColumnType("decimal(18,3)");
        builder.Property(x => x.OrderedQty).HasColumnType("decimal(18,3)");
        builder.Property(x => x.ReceivedQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.DeliveryUoM).HasMaxLength(50).IsRequired();
        builder.Property(x => x.LineAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.RemainOption).HasMaxLength(20);
        builder.Property(x => x.IsClosed).HasDefaultValue(false);

        builder.HasOne(x => x.PurchaseOrder)
            .WithMany(p => p.Details)
            .HasForeignKey(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
