using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Sales;

public class VoucherCodeConfiguration : IEntityTypeConfiguration<VoucherCode>
{
    public void Configure(EntityTypeBuilder<VoucherCode> builder)
    {
        builder.ToTable("VoucherCodes");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("VoucherCodeID");

        builder.Property(x => x.TenantId).HasColumnName("TenantID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.PromotionId).HasColumnName("PromotionID").IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).IsRequired().HasDefaultValue("UNUSED");
        builder.Property(x => x.CustomerId).HasColumnName("CustomerID");
        builder.Property(x => x.CustomerName).HasMaxLength(100);
        builder.Property(x => x.CustomerPhone).HasMaxLength(20);
        builder.Property(x => x.OrderId).HasColumnName("OrderID");
        builder.Property(x => x.OrderCode).HasMaxLength(50);
        builder.Property(x => x.UsedAt);

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.PromotionId, x.Status });
    }
}

