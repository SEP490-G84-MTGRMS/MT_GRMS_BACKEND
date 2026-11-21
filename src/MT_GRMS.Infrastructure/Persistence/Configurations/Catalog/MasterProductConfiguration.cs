using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Catalog;

public class MasterProductConfiguration : IEntityTypeConfiguration<MasterProduct>
{
    public void Configure(EntityTypeBuilder<MasterProduct> builder)
    {
        builder.ToTable("MasterProducts");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("MasterProductID");

        builder.Property(x => x.Barcode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(250).IsRequired();
        builder.Property(x => x.Brand).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CategoryName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.BaseUnit).HasMaxLength(30).IsRequired();
        builder.Property(x => x.SuggestedRetailPrice).HasColumnType("decimal(18,2)");
        builder.Property(x => x.VatRate).HasColumnType("decimal(5,2)").HasDefaultValue(8.00m);
        builder.Property(x => x.TaxCategory).HasMaxLength(50).HasDefaultValue("STANDARD_1PCT");
        builder.Property(x => x.ImageUrl).HasMaxLength(1000);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.UsageCount).HasDefaultValue(0);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);

        // Unique index cho Barcode trên Master Catalog (chỉ áp dụng cho bản ghi chưa bị xóa)
        builder.HasIndex(x => x.Barcode)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasMany(x => x.Units)
            .WithOne(u => u.MasterProduct)
            .HasForeignKey(u => u.MasterProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
