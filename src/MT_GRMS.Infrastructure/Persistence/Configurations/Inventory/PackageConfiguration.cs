using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Inventory;

public class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.ToTable("Packages");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.InternalCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Barcode).HasMaxLength(50);
        builder.Property(x => x.RepackType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.VoidMode).HasMaxLength(30);
        builder.Property(x => x.VoidReason).HasMaxLength(500);
        builder.Property(x => x.PriceHistoryJson).HasColumnType("text");

        builder.Property(x => x.NetWeight).HasColumnType("decimal(18,3)");
        builder.Property(x => x.SellingPrice).HasColumnType("decimal(18,2)");

        builder.HasIndex(x => new { x.TenantId, x.InternalCode }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Barcode })
            .IsUnique()
            .HasFilter("\"Barcode\" IS NOT NULL");
        builder.HasIndex(x => new { x.BranchStockId, x.Status });

        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.BranchStock).WithMany().HasForeignKey(x => x.BranchStockId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RepackOrder).WithMany(x => x.Packages).HasForeignKey(x => x.RepackOrderId).OnDelete(DeleteBehavior.SetNull);
    }
}
