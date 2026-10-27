using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("PurchaseOrders");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SupplierId).IsRequired();
        builder.Property(x => x.BranchId).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).HasDefaultValue("DRAFT");
        builder.Property(x => x.TotalQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.CreatedByRole).HasMaxLength(30);
        builder.Property(x => x.SubmittedByUserId).HasMaxLength(50);
        builder.Property(x => x.ApprovedByUserId).HasMaxLength(50);
        builder.Property(x => x.RejectedByUserId).HasMaxLength(50);
        builder.Property(x => x.RejectionReason).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(1);

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

        builder.HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ParentDraft)
            .WithMany()
            .HasForeignKey(x => x.ParentDraftId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SubmittedByUser)
            .WithMany()
            .HasForeignKey(x => x.SubmittedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.ApprovedByUser)
            .WithMany()
            .HasForeignKey(x => x.ApprovedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.RejectedByUser)
            .WithMany()
            .HasForeignKey(x => x.RejectedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
