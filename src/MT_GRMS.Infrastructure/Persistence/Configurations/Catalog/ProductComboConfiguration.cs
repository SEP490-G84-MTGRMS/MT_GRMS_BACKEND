using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Catalog;

public class ProductComboConfiguration : IEntityTypeConfiguration<ProductCombo>
{
    public void Configure(EntityTypeBuilder<ProductCombo> builder)
    {
        builder.ToTable("ProductCombos");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ComboID");

        builder.Property(x => x.TenantId).HasColumnName("TenantID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.ComboProductId).HasColumnName("ComboProductID").IsRequired();
        builder.Property(x => x.ComponentProductId).HasColumnName("ComponentProductID").IsRequired();
        builder.Property(x => x.Quantity).HasColumnType("decimal(12,2)").HasDefaultValue(1.00m);
        builder.Property(x => x.Note).HasMaxLength(255);

        builder.HasIndex(x => new { x.TenantId, x.ComboProductId, x.ComponentProductId }).IsUnique();

        builder.HasOne(x => x.ComboProduct)
            .WithMany(p => p.ComboComponents)
            .HasForeignKey(x => x.ComboProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ComponentProduct)
            .WithMany()
            .HasForeignKey(x => x.ComponentProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
