using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class PurchaseOrderRevisionConfiguration : IEntityTypeConfiguration<PurchaseOrderRevision>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderRevision> builder)
    {
        builder.ToTable("PurchaseOrderRevisions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PreviousDataSnapshotJson).HasColumnType("text");
        builder.Property(x => x.NewDataSnapshotJson).HasColumnType("text");
        builder.Property(x => x.ChangedByUserId).HasMaxLength(50);
        builder.Property(x => x.ChangeReason).HasMaxLength(500);

        builder.HasIndex(x => new { x.TenantId, x.PurchaseOrderId, x.RevisionNumber }).IsUnique();

        builder.HasOne(x => x.PurchaseOrder)
            .WithMany()
            .HasForeignKey(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
