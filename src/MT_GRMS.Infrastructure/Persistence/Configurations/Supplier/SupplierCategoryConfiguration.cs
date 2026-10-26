using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class SupplierCategoryConfiguration : IEntityTypeConfiguration<SupplierCategory>
{
    public void Configure(EntityTypeBuilder<SupplierCategory> builder)
    {
        builder.ToTable("supplier_categories");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CategoryCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CategoryName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);

        builder.HasIndex(x => new { x.TenantId, x.CategoryCode }).IsUnique();
    }
}
