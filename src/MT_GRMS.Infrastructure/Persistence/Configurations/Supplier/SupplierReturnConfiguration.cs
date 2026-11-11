using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class SupplierReturnConfiguration : IEntityTypeConfiguration<SupplierReturn>
{
    public void Configure(EntityTypeBuilder<SupplierReturn> builder)
    {
        builder.ToTable("SupplierReturns");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.GoodsReceiptId).IsRequired();
        builder.Property(x => x.SupplierId).IsRequired();
        builder.Property(x => x.BranchId).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("DRAFT");
        builder.Property(x => x.Reason).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonNotes).HasMaxLength(1000);
        builder.Property(x => x.SupplierResponseNote).HasMaxLength(1000);
        builder.Property(x => x.CancellationReason).HasMaxLength(500);
        builder.Property(x => x.AttachmentUrl).HasMaxLength(1000);
        builder.Property(x => x.StocktakeCode).HasMaxLength(50);
        builder.Property(x => x.ResolutionType).HasMaxLength(50).HasDefaultValue("REDUCE_PAYABLE");
        builder.Property(x => x.SettlementStatus).HasMaxLength(50).HasDefaultValue("UNSETTLED");

        builder.Property(x => x.TotalRequestedQty).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(x => x.TotalApprovedQty).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(x => x.TotalHandedOverQty).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(x => x.TotalEstimatedAmount).HasColumnType("decimal(18,2)").IsRequired();

        builder.Property(x => x.HandedOverBy).HasMaxLength(100);

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.BranchId });
        builder.HasIndex(x => new { x.TenantId, x.SupplierId });
        builder.HasIndex(x => new { x.TenantId, x.Status });

        builder.HasOne(x => x.GoodsReceipt)
            .WithMany()
            .HasForeignKey(x => x.GoodsReceiptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne(l => l.SupplierReturn)
            .HasForeignKey(l => l.SupplierReturnId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

