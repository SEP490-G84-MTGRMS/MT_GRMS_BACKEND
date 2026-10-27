using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Inventory;

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
        builder.Property(x => x.Status).HasMaxLength(30).HasDefaultValue("Draft");
        builder.Property(x => x.CreatedByStaff).HasMaxLength(100).IsRequired();
        builder.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.DiscrepancyNote).HasMaxLength(500);
        builder.Property(x => x.CancelReason).HasMaxLength(255);
        builder.Property(x => x.IsFavorite).HasDefaultValue(false);

        builder.HasIndex(x => x.TransferCode).IsUnique();

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
