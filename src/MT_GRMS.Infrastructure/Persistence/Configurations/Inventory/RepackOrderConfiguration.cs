using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Inventory;

public class RepackOrderConfiguration : IEntityTypeConfiguration<RepackOrder>
{
    public void Configure(EntityTypeBuilder<RepackOrder> builder)
    {
        builder.ToTable("RepackOrders");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.RepackType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.WasteReason).HasMaxLength(40);
        builder.Property(x => x.WasteNote).HasMaxLength(500);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();

        builder.Property(x => x.InputWeight).HasColumnType("decimal(18,3)");
        builder.Property(x => x.WasteWeight).HasColumnType("decimal(18,3)");
        builder.Property(x => x.UnitCostSnapshot).HasColumnType("decimal(18,2)");
        builder.Property(x => x.WasteCostAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.DefaultUnitPricePerKg).HasColumnType("decimal(18,2)");

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.BranchStockId });

        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.BranchStock).WithMany().HasForeignKey(x => x.BranchStockId).OnDelete(DeleteBehavior.Restrict);
    }
}
