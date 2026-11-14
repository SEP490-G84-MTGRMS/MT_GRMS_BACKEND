using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class SupplierReturnPolicyConfiguration : IEntityTypeConfiguration<SupplierReturnPolicy>
{
    public void Configure(EntityTypeBuilder<SupplierReturnPolicy> builder)
    {
        builder.ToTable("SupplierReturnPolicies");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AllowedReturnTypesJson).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.AllowedResolutionTypesJson).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.DefaultResolutionType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CostShareBase).HasMaxLength(32).IsRequired();
        builder.Property(x => x.StoreCostSharePercent).HasColumnType("decimal(5,2)");
        builder.Property(x => x.SupplierCostSharePercent).HasColumnType("decimal(5,2)");
        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.HasIndex(x => new { x.TenantId, x.SupplierId, x.IsActive });
        builder.HasIndex(x => new { x.TenantId, x.SupplierId })
            .IsUnique()
            .HasFilter("\"IsActive\" = true");

        builder.HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
