using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class SupplierBranchAssignmentConfiguration : IEntityTypeConfiguration<SupplierBranchAssignment>
{
    public void Configure(EntityTypeBuilder<SupplierBranchAssignment> builder)
    {
        builder.ToTable("supplier_branch_assignments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired()
            .HasDefaultValue(SupplierStatus.Active);

        builder.Property(x => x.InactiveReason).HasMaxLength(500);

        builder.HasIndex(x => new { x.TenantId, x.SupplierId, x.BranchId }).IsUnique();

        builder.HasOne(x => x.Supplier)
            .WithMany(s => s.BranchAssignments)
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
