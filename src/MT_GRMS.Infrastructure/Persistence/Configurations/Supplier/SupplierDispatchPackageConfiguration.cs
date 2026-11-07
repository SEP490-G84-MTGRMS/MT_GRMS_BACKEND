using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class SupplierDispatchPackageConfiguration : IEntityTypeConfiguration<SupplierDispatchPackage>
{
    public void Configure(EntityTypeBuilder<SupplierDispatchPackage> builder)
    {
        builder.ToTable("SupplierDispatchPackages");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SupplierCodeSnapshot).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SupplierNameSnapshot).HasMaxLength(250).IsRequired();
        builder.Property(x => x.EmailTo).HasMaxLength(200).IsRequired();
        builder.Property(x => x.EmailCcJson).HasColumnType("text").IsRequired();
        builder.Property(x => x.EmailSendMode).HasMaxLength(30).IsRequired();
        builder.Property(x => x.EmailStatus).HasMaxLength(30).IsRequired();
        builder.Property(x => x.GenerationStatus).HasMaxLength(30).IsRequired();
        builder.Property(x => x.PackageRevisionNumber).HasDefaultValue(1);
        builder.Property(x => x.FileName).HasMaxLength(255);
        builder.Property(x => x.FileStorageKey).HasMaxLength(500);
        builder.Property(x => x.FileChecksum).HasMaxLength(128);
        builder.Property(x => x.LastError).HasMaxLength(2000);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100);
        builder.Property(x => x.ProcessingLockedBy).HasMaxLength(100);

        builder.HasIndex(x => new { x.TenantId, x.DispatchBatchId, x.SupplierId });
        builder.HasIndex(x => new { x.TenantId, x.EmailStatus, x.ScheduledSendAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.GenerationStatus });
        builder.HasIndex(x => x.IdempotencyKey);

        builder.HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Items)
            .WithOne(i => i.Package!)
            .HasForeignKey(i => i.PackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SupplierDispatchPackageItemConfiguration : IEntityTypeConfiguration<SupplierDispatchPackageItem>
{
    public void Configure(EntityTypeBuilder<SupplierDispatchPackageItem> builder)
    {
        builder.ToTable("SupplierDispatchPackageItems");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();

        // Một PO chỉ thuộc 1 package item active; package cũ soft-delete item khi amend.
        builder.HasIndex(x => x.PurchaseOrderId)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(x => new { x.PackageId, x.PurchaseOrderId }).IsUnique();

        builder.HasOne(x => x.PurchaseOrder)
            .WithMany()
            .HasForeignKey(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
