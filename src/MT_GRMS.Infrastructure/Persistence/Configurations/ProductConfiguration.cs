using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ProductID");

        builder.Property(x => x.TenantId).HasColumnName("TenantID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.CategoryId).HasColumnName("CategoryID").IsRequired();
        builder.Property(x => x.ProductCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Barcode).HasMaxLength(50).IsRequired(false);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.BaseUnit).HasMaxLength(30).IsRequired();
        builder.Property(x => x.CostPrice).HasColumnType("decimal(18,2)");
        builder.Property(x => x.SellingPrice).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.MinStock).HasColumnType("decimal(12,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.MaxStock).HasColumnType("decimal(12,2)").HasDefaultValue(1000.00m);
        builder.Property(x => x.VatRate).HasColumnType("decimal(5,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.ProductType).HasMaxLength(30).HasDefaultValue("STANDARD");
        builder.Property(x => x.Status).HasMaxLength(30).HasDefaultValue("Active");
        builder.Property(x => x.ImageUrl).HasMaxLength(500);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.InternalNote).HasMaxLength(500);
        builder.Property(x => x.HasBatches).HasDefaultValue(false);
        builder.Property(x => x.IsFavorite).HasDefaultValue(false);

        builder.HasIndex(x => x.Barcode).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.ProductCode }).IsUnique();

        builder.HasOne(x => x.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
