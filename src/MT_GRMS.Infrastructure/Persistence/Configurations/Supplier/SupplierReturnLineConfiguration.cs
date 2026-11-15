using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class SupplierReturnLineConfiguration : IEntityTypeConfiguration<SupplierReturnLine>
{
    public void Configure(EntityTypeBuilder<SupplierReturnLine> builder)
    {
        builder.ToTable("SupplierReturnLines");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SupplierReturnId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.BatchCode).HasMaxLength(100);

        builder.Property(x => x.RequestedQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.SupplierAcceptedQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.ApprovedQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.RejectedQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.HandedOverQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.ExchangePoOrderedQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.UnitCost).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.ReplacementUnitPrice).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);

        builder.Property(x => x.RejectionReason).HasMaxLength(500);
        builder.Property(x => x.Notes).HasMaxLength(500);

        builder.HasIndex(x => x.SupplierReturnId);
        builder.HasIndex(x => x.ProductId);
        builder.HasIndex(x => new { x.TenantId, x.ProductId, x.BatchCode });

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

