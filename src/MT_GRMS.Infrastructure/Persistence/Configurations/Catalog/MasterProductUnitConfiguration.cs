using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Catalog;

public class MasterProductUnitConfiguration : IEntityTypeConfiguration<MasterProductUnit>
{
    public void Configure(EntityTypeBuilder<MasterProductUnit> builder)
    {
        builder.ToTable("MasterProductUnits");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("MasterProductUnitID");

        builder.Property(x => x.MasterProductId).HasColumnName("MasterProductID").IsRequired();
        builder.Property(x => x.UnitName).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ConversionRate).HasColumnType("decimal(18,4)").HasDefaultValue(1.00m);
        builder.Property(x => x.Barcode).HasMaxLength(50);
        builder.Property(x => x.IsPurchaseUnit).HasDefaultValue(true);
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(x => x.MasterProductId);
        builder.HasIndex(x => x.Barcode);
    }
}
