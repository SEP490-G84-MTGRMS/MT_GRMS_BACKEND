using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Inventory;

public class TransferConfiguration : IEntityTypeConfiguration<Transfer>
{
    public void Configure(EntityTypeBuilder<Transfer> builder)
    {
        builder.ToTable("Transfers");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("TransferID");

        builder.Property(x => x.TransferCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.FromBranchId).HasColumnName("FromBranchID").IsRequired();
        builder.Property(x => x.ToBranchId).HasColumnName("ToBranchID").IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).HasDefaultValue(TransferStatus.Draft).HasSentinel((TransferStatus)0).IsConcurrencyToken();
        builder.Property(x => x.CompletionResult).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.Origin).HasConversion<string>().HasMaxLength(30).HasDefaultValue(TransferOrigin.OwnerAllocation).ValueGeneratedNever();
        builder.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20).HasDefaultValue(TransferPriority.Normal).ValueGeneratedNever();
        builder.Property(x => x.CreatedByStaff).HasMaxLength(100).IsRequired();
        builder.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.DiscrepancyNote).HasMaxLength(500);
        builder.Property(x => x.CancelReason).HasMaxLength(255);
        builder.Property(x => x.IsFavorite).HasDefaultValue(false);
        builder.Property(x => x.HasDiscrepancy).HasDefaultValue(false);
        builder.Property(x => x.IsStockReserved).HasDefaultValue(false);
        builder.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(1);

        builder.HasIndex(x => new { x.TenantId, x.TransferCode }).IsUnique();

        builder.HasOne(x => x.FromBranch)
            .WithMany()
            .HasForeignKey(x => x.FromBranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ToBranch)
            .WithMany()
            .HasForeignKey(x => x.ToBranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
