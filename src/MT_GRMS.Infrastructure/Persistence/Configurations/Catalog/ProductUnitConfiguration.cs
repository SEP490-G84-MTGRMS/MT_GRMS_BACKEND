using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Catalog;

public class ProductUnitConfiguration : IEntityTypeConfiguration<ProductUnit>
{
    public void Configure(EntityTypeBuilder<ProductUnit> builder)
    {
        builder.ToTable("ProductUnits");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("UnitID");

        builder.Property(x => x.ProductId).HasColumnName("ProductID").IsRequired();
        builder.Property(x => x.UnitName).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ConversionRate).HasColumnType("decimal(10,2)").HasDefaultValue(1.00m);
        builder.Property(x => x.Price).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.Barcode).HasMaxLength(50);
        builder.Property(x => x.IsBaseUnit).HasDefaultValue(false);

        builder.HasOne(x => x.Product)
            .WithMany(p => p.Units)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
