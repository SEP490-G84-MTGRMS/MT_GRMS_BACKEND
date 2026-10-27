using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Sales;

public class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        builder.ToTable("Promotions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("PromotionID");

        builder.Property(x => x.TenantId).HasColumnName("TenantID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.PromotionCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PromotionName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DiscountValue).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => new { x.TenantId, x.PromotionCode }).IsUnique();
    }
}
