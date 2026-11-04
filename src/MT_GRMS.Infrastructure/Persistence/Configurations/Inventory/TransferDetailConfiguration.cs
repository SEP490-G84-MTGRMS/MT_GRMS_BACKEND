using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Inventory;

public class TransferDetailConfiguration : IEntityTypeConfiguration<TransferDetail>
{
    public void Configure(EntityTypeBuilder<TransferDetail> builder)
    {
        builder.ToTable("TransferDetails");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("TransferDetailID");

        builder.Property(x => x.TransferId).HasColumnName("TransferID").IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.SentQuantity).HasColumnType("decimal(12,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.ReceivedQuantity).HasColumnType("decimal(12,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.DamagedQuantity).HasColumnType("decimal(12,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.PackagingUnit).HasMaxLength(30).IsRequired();
        builder.Property(x => x.BatchNumber).HasMaxLength(50);
        builder.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.DiscrepancyReason).HasMaxLength(255);
        builder.Property(x => x.Note).HasMaxLength(255);

        builder.HasOne(x => x.Transfer)
            .WithMany(t => t.Details)
            .HasForeignKey(x => x.TransferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
